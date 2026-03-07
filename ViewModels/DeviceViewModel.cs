using CommunityToolkit.Mvvm.ComponentModel;
using RadioLogger.Models;
using RadioLogger.Services;

namespace RadioLogger.ViewModels
{
    public partial class DeviceViewModel : ObservableObject
    {
        private readonly AudioEngine _audioEngine;
        private readonly ConfigManager _configManager;
        private AudioChannel? _activeChannel;

        [ObservableProperty]
        private AudioDevice _device;

        [ObservableProperty]
        private string _stationName = string.Empty;

        [ObservableProperty]
        private string _recordingDuration = "0d 00h 00m 00s";

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private bool _isRecording;

        [ObservableProperty]
        private bool _isStreaming;

        [ObservableProperty]
        private bool _isReconnecting;

        [ObservableProperty]
        private System.DateTime? _recordingStartTime;

        [ObservableProperty]
        private double _leftLevel;

        [ObservableProperty]
        private double _rightLevel;

        public DeviceViewModel(AudioDevice device, AudioEngine audioEngine, ConfigManager configManager, string stationName = null, bool initiallySelected = false)
        {
            _device = device;
            _audioEngine = audioEngine;
            _configManager = configManager;
            _isSelected = initiallySelected;
            StationName = !string.IsNullOrWhiteSpace(stationName) ? stationName : device.Name;
            
            // Sync with Reality (Engine Source of Truth)
            if (_audioEngine.IsDeviceRecording(device.Id))
            {
                _isSelected = true; // Set backing field to avoid triggering toggle logic yet
                IsRecording = true;
                RecordingStartTime = _audioEngine.GetDeviceStartTime(device.Id);
                IsStreaming = _audioEngine.IsDeviceStreaming(device.Id);
                IsReconnecting = _audioEngine.IsDeviceReconnecting(device.Id);
                
                // Re-attach to the active channel to get levels
                _activeChannel = _audioEngine.StartRecording(device, StationName);
            }
        }

        public void ToggleStreaming()
        {
            if (IsStreaming || IsReconnecting)
            {
                // Stop (works for both active stream and pending reconnect)
                _audioEngine.StopStreaming(Device);
                IsStreaming = false;
                IsReconnecting = false;
            }
            else
            {
                // Start
                
                // Auto-Start Recording if OFF
                if (!IsRecording) 
                {
                    IsSelected = true;
                    UpdateState(); // Forces ON AIR
                }

                if (_configManager.CurrentSettings.DeviceStreamingConfigs.TryGetValue(Device.Name, out var config))
                {
                    if (config.IsEnabled)
                    {
                        _audioEngine.StartStreaming(Device, config);
                        IsStreaming = true;
                    }
                    else
                    {
                        // Config exists but disabled? Or just start?
                        // Let's assume we start if it exists.
                        _audioEngine.StartStreaming(Device, config);
                        IsStreaming = true;
                    }
                }
                else
                {
                    // No config found, maybe create default?
                    var newConfig = new StreamingConfig();
                    _configManager.CurrentSettings.DeviceStreamingConfigs[Device.Name] = newConfig;
                    _audioEngine.StartStreaming(Device, newConfig);
                    IsStreaming = true;
                }
            }
        }

        public void UpdateState()
        {
            if (IsSelected && !IsRecording)
            {
                // User checked the box, start recording
                _activeChannel = _audioEngine.StartRecording(Device, StationName);
                if (_activeChannel != null)
                {
                    IsRecording = true;
                    RecordingStartTime = System.DateTime.Now;
                }
            }
            else if (!IsSelected && IsRecording)
            {
                // User unchecked, stop
                _audioEngine.StopRecording(Device);
                _activeChannel = null;
                IsRecording = false;
                RecordingStartTime = null;
                LeftLevel = 0;
                RightLevel = 0;
            }
        }

        [ObservableProperty]
        private double _leftPeak;
        
        [ObservableProperty]
        private double _rightPeak;

        [ObservableProperty]
        private bool _isSilenceDetected;
        
        [ObservableProperty]
        private double _inputVolume = 100; // 0-150, Default 100 (Unit Gain)

        partial void OnInputVolumeChanged(double value)
        {
            // Software Digital Gain (DSP)
            if (_audioEngine != null)
            {
                 // Value 100 = 1.0x (No change)
                 // Value 150 = 1.5x (Boost)
                 // Value 0 = 0.0x (Mute)
                 _audioEngine.SetChannelGain(Device, (float)(value / 100.0));
            }
        }

        // Peak Hold logic vars
        private double _internalLeftPeak;
        private double _internalRightPeak;
        private int _leftHoldCounter;
        private int _rightHoldCounter;
        private const int HoldFrames = 10; 
        private const double DecayRate = 2.0;
        
        // Silence Detection vars
        private int _silenceCounter;
        private const int SilenceThresholdFrames = 200; // 10 seconds at 20fps
        private const double SilenceDbThreshold = -40.0;

        public void RefreshLevels()
        {
            // Sync Streaming State from Engine
            bool realStreamingState = _audioEngine.IsDeviceStreaming(Device.Id);
            if (IsStreaming != realStreamingState)
            {
                IsStreaming = realStreamingState;
            }

            bool realReconnectState = _audioEngine.IsDeviceReconnecting(Device.Id);
            if (IsReconnecting != realReconnectState)
            {
                IsReconnecting = realReconnectState;
            }

            // Update Timer
            if (IsRecording && RecordingStartTime.HasValue)
            {
                var diff = System.DateTime.Now - RecordingStartTime.Value;
                // Always show days: 00d 00h 00m 00s
                RecordingDuration = $"{diff.Days:D2}d {diff.Hours:D2}h {diff.Minutes:D2}m {diff.Seconds:D2}s";
            }
            else
            {
                RecordingDuration = "00d 00h 00m 00s";
            }

            if (IsRecording && _activeChannel != null)
            {
                // ... (Existing logic) ...
                var lDb = ToDbPercentage(_activeChannel.LeftLevel);
                var rDb = ToDbPercentage(_activeChannel.RightLevel);

                // Convert back to real dB for silence check (approx)
                // Since ToDbPercentage returns 0-100 mapped from -60 to 0
                // -40dB is approx 33% in our scale (( -40 + 60 ) / 60 * 100)
                
                double realDbLeft = (lDb * 0.6) - 60;
                double realDbRight = (rDb * 0.6) - 60;
                
                if (realDbLeft < SilenceDbThreshold && realDbRight < SilenceDbThreshold)
                {
                    _silenceCounter++;
                    if (_silenceCounter > SilenceThresholdFrames)
                    {
                        IsSilenceDetected = true;
                    }
                }
                else
                {
                    _silenceCounter = 0;
                    IsSilenceDetected = false;
                }

                LeftLevel = lDb;
                RightLevel = rDb;

                // Peak Logic Left
                if (lDb > _internalLeftPeak)
                {
                    _internalLeftPeak = lDb;
                    _leftHoldCounter = HoldFrames;
                }
                else
                {
                    if (_leftHoldCounter > 0)
                        _leftHoldCounter--;
                    else
                    {
                        _internalLeftPeak -= DecayRate;
                        if (_internalLeftPeak < 0) _internalLeftPeak = 0;
                    }
                }
                LeftPeak = _internalLeftPeak;

                // Peak Logic Right
                if (rDb > _internalRightPeak)
                {
                    _internalRightPeak = rDb;
                    _rightHoldCounter = HoldFrames;
                }
                else
                {
                    if (_rightHoldCounter > 0)
                        _rightHoldCounter--;
                    else
                    {
                        _internalRightPeak -= DecayRate;
                        if (_internalRightPeak < 0) _internalRightPeak = 0;
                    }
                }
                RightPeak = _internalRightPeak;

                // Clipping detection (if raw signal > 0.99)
                IsClipping = _activeChannel.LeftLevel > 0.99 || _activeChannel.RightLevel > 0.99;
            }
            else
            {
                LeftLevel = 0;
                RightLevel = 0;
                LeftPeak = 0;
                RightPeak = 0;
                IsClipping = false;
            }
        }

        [ObservableProperty]
        private bool _isClipping;

        private double ToDbPercentage(float value)
        {
            if (value <= 0.001f) return 0; // Noise floor / Silence

            double db = 20 * Math.Log10(value);

            // Clamp to our scale: -60dB to 0dB
            if (db < -60) db = -60;
            if (db > 0) db = 0;

            // Map -60...0 range to 0...100%
            return ((db + 60) / 60) * 100;
        }
    }
}
