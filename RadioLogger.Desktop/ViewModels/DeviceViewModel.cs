using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioLogger.Models;
using RadioLogger.Services;
using System;

namespace RadioLogger.ViewModels
{
    public partial class DeviceViewModel : ObservableObject
    {
        private readonly AudioDevice _device;
        private readonly AudioEngine _audioEngine;
        private readonly ConfigManager _configManager;
        private AudioChannel? _activeChannel;

        public AudioDevice Device => _device;

        [ObservableProperty]
        private string _stationName;

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private double _leftLevel;

        [ObservableProperty]
        private double _rightLevel;

        [ObservableProperty]
        private double _leftPeak;

        [ObservableProperty]
        private double _rightPeak;

        [ObservableProperty]
        private bool _isClipping;

        [ObservableProperty]
        private bool _isStreaming;

        [ObservableProperty]
        private bool _isReconnecting;

        [ObservableProperty]
        private string? _streamUrl;

        [ObservableProperty]
        private bool _isRecording;

        [ObservableProperty]
        private string _recordingDuration = "00d 00h 00m 00s";

        [ObservableProperty]
        private DateTime? _recordingStartTime;

        [ObservableProperty]
        private bool _isSilenceDetected;

        [ObservableProperty]
        private string _silenceDuration = "00:00";
        
        [ObservableProperty]
        private double _inputVolume = 100; // 0-150, Default 100 (Unit Gain)

        [ObservableProperty]
        private bool _isRecordingEnabled = true;

        // Peak Hold logic vars
        private double _internalLeftPeak;
        private double _internalRightPeak;
        private int _leftHoldCounter;
        private int _rightHoldCounter;
        private const int HoldFrames = 10; 
        private const double DecayRate = 2.0;
        
        // Silence Detection vars
        private int _silenceCounter;
        private int _restoreCounter;
        private System.DateTime? _silenceStartTime;
        private const int SilenceThresholdFrames = 200; // 10 seconds at 20fps
        private const int RestoreThresholdFrames = 60;  // 3 seconds at 20fps
        private const double SilenceDbThreshold = -40.0;

        public DeviceViewModel(AudioDevice device, AudioEngine audioEngine, ConfigManager configManager, string stationName, bool isSelected)
        {
            _device = device;
            _audioEngine = audioEngine;
            _configManager = configManager;
            _stationName = stationName;
            _isSelected = isSelected;

            // Load per-device recording enabled setting
            if (_configManager.CurrentSettings.DeviceRecordingEnabled.TryGetValue(device.Name, out bool recEnabled))
                IsRecordingEnabled = recEnabled;
            else
                IsRecordingEnabled = true; // Default: enabled

            // Sync initial state if engine already has this device active
            if (_audioEngine.IsDeviceRecording(device.Id))
            {
                IsRecording = true;
                RecordingStartTime = _audioEngine.GetDeviceStartTime(device.Id);
            }
        }

        public void UpdateState()
        {
            if (IsSelected)
            {
                _activeChannel = _audioEngine.StartRecording(_device, _stationName, IsRecordingEnabled);
                IsRecording = _activeChannel != null;
                if (IsRecording) RecordingStartTime = DateTime.Now;
            }
            else
            {
                _audioEngine.StopRecording(_device);
                IsRecording = false;
                _activeChannel = null;
                RecordingStartTime = null;
            }
        }

        public void ToggleStreaming()
        {
            if (!IsRecording) return;

            if (IsStreaming)
            {
                _audioEngine.StopStreaming(_device);
            }
            else
            {
                if (_configManager.CurrentSettings.DeviceStreamingConfigs.TryGetValue(_device.Name, out var config))
                {
                    _audioEngine.StartStreaming(_device, config);
                }
            }
        }

        partial void OnInputVolumeChanged(double value)
        {
            if (_activeChannel != null)
            {
                _activeChannel.SetGain((float)(value / 100.0));
            }
        }

        public void RefreshLevels()
        {
            // Auto-link channel if recording but reference is null
            if (IsRecording && _activeChannel == null)
            {
                _activeChannel = _audioEngine.StartRecording(_device, _stationName, IsRecordingEnabled);
            }

            // Sync States
            IsStreaming = _audioEngine.IsDeviceStreaming(Device.Id);
            IsReconnecting = _audioEngine.IsDeviceReconnecting(Device.Id);
            StreamUrl = _audioEngine.GetDeviceStreamUrl(Device.Id);

            // Update Recording Timer
            if (IsRecording && RecordingStartTime.HasValue)
            {
                var diff = System.DateTime.Now - RecordingStartTime.Value;
                RecordingDuration = $"{diff.Days:D2}d {diff.Hours:D2}h {diff.Minutes:D2}m {diff.Seconds:D2}s";
            }
            else
            {
                RecordingDuration = "00d 00h 00m 00s";
            }

            if (IsRecording && _activeChannel != null)
            {
                var lLevel = _activeChannel.LeftLevel;
                var rLevel = _activeChannel.RightLevel;
                
                var lDb = ToDbPercentage(lLevel);
                var rDb = ToDbPercentage(rLevel);

                // Silence detection logic
                double realDbLeft = (lDb * 0.6) - 60;
                double realDbRight = (rDb * 0.6) - 60;
                
                if (realDbLeft < SilenceDbThreshold && realDbRight < SilenceDbThreshold)
                {
                    _restoreCounter = 0; // Reset restore if we see silence
                    _silenceCounter++;
                    if (_silenceCounter > SilenceThresholdFrames)
                    {
                        if (!IsSilenceDetected)
                        {
                            IsSilenceDetected = true;
                            _silenceStartTime = System.DateTime.Now;
                        }

                        if (_silenceStartTime.HasValue)
                        {
                            var diff = System.DateTime.Now - _silenceStartTime.Value;
                            SilenceDuration = $"{diff.Minutes:D2}:{diff.Seconds:D2}";
                        }
                    }
                }
                else
                {
                    _silenceCounter = 0;
                    
                    if (IsSilenceDetected)
                    {
                        // We are in silence state, but we see audio. 
                        // Wait for RestoreThresholdFrames (3s) before declaring it restored.
                        _restoreCounter++;
                        if (_restoreCounter > RestoreThresholdFrames)
                        {
                            IsSilenceDetected = false;
                            _silenceStartTime = null;
                            // We don't reset SilenceDuration here so Telegram can read the final value.
                            // It will be reset when a new silence starts.
                            _restoreCounter = 0;
                        }
                    }
                    else
                    {
                        // Normal state
                        IsSilenceDetected = false;
                        _silenceStartTime = null;
                        SilenceDuration = "00:00";
                        _restoreCounter = 0;
                    }
                }

                LeftLevel = lDb;
                RightLevel = rDb;

                // Peak Hold logic
                if (lDb >= _internalLeftPeak) { _internalLeftPeak = lDb; _leftHoldCounter = HoldFrames; }
                else { if (_leftHoldCounter > 0) _leftHoldCounter--; else _internalLeftPeak = Math.Max(0, _internalLeftPeak - DecayRate); }

                if (rDb >= _internalRightPeak) { _internalRightPeak = rDb; _rightHoldCounter = HoldFrames; }
                else { if (_rightHoldCounter > 0) _rightHoldCounter--; else _internalRightPeak = Math.Max(0, _internalRightPeak - DecayRate); }

                LeftPeak = _internalLeftPeak;
                RightPeak = _internalRightPeak;
                IsClipping = (lDb > 98 || rDb > 98);
            }
            else
            {
                // Reset State
                _silenceCounter = 0;
                _restoreCounter = 0;
                IsSilenceDetected = false;
                _silenceStartTime = null;
                SilenceDuration = "00:00";
                LeftLevel = 0;
                RightLevel = 0;
                LeftPeak = 0;
                RightPeak = 0;
                IsClipping = false;
                _internalLeftPeak = 0;
                _internalRightPeak = 0;
            }
        }

        private double ToDbPercentage(double value)
        {
            if (value <= 0) return 0;
            double db = 20 * Math.Log10(value);
            if (db < -60) db = -60;
            if (db > 0) db = 0;
            return ((db + 60) / 60) * 100;
        }
    }
}
