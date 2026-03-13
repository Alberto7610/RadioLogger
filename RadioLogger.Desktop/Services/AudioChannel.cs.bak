using ManagedBass;
using ManagedBass.Enc;
using RadioLogger.Models;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Timers;

namespace RadioLogger.Services
{
    public class AudioChannel : IDisposable
    {
        private int _handle; 
        private int _deviceId;
        private readonly AppSettings _settings;
        private readonly System.Timers.Timer _levelTimer;
        private readonly System.Timers.Timer _fileRotatorTimer;
        private readonly System.Timers.Timer _reconnectTimer;
        
        private int _encoderHandle;
        
        // Delegates
        private RecordProcedure _recordProcedure;
        private DSPProcedure _gainDsp;
        private int _dspHandle;
        private float _currentGain = 1.0f;

        private DateTime _currentFileDate;
        private string? _currentFilePath;

        public float LeftLevel { get; private set; }
        public float RightLevel { get; private set; }
        public bool IsRecording { get; private set; }
        public bool IsStreaming { get; private set; }
        public bool IsReconnecting { get; private set; }
        public string? StreamUrl => _currentConfig?.GetPublicUrl();
        public DateTime StartTime { get; private set; }
        
        public AudioDevice DeviceInfo { get; private set; }
        
        private string _stationName = string.Empty;
        public string StationName 
        { 
            get => _stationName;
            set
            {
                if (_stationName != value)
                {
                    _stationName = value;
                    if (IsRecording)
                    {
                        StartNewFile();
                    }
                }
            }
        }

        private StreamingClient? _streamingClient;
        private StreamingConfig? _currentConfig;

        public AudioChannel(AudioDevice device, string stationName, AppSettings settings)
        {
            DeviceInfo = device;
            _deviceId = device.Id;
            _stationName = stationName;
            _settings = settings;

            _recordProcedure = new RecordProcedure(RecordingCallback);
            _gainDsp = new DSPProcedure(GainCallback);

            _levelTimer = new System.Timers.Timer(50); 
            _levelTimer.Elapsed += UpdateLevels;
            
            _fileRotatorTimer = new System.Timers.Timer(1000); 
            _fileRotatorTimer.Elapsed += CheckFileRotation;

            _reconnectTimer = new System.Timers.Timer(10000); // 10s retry
            _reconnectTimer.AutoReset = false;
            _reconnectTimer.Elapsed += OnReconnectTimerElapsed;
        }

        public void SetGain(float gain)
        {
            _currentGain = gain;
        }

        private unsafe void GainCallback(int handle, int channel, IntPtr buffer, int length, IntPtr user)
        {
            if (_currentGain == 1.0f) return;

            short* data = (short*)buffer;
            int samples = length / 2;

            for (int i = 0; i < samples; i++)
            {
                int s = (int)(data[i] * _currentGain);
                if (s > 32767) s = 32767;
                else if (s < -32768) s = -32768;
                data[i] = (short)s;
            }
        }

        private bool RecordingCallback(int handle, IntPtr buffer, int length, IntPtr user)
        {
            return true; 
        }

        public bool Initialize()
        {
            if (!Bass.RecordInit(_deviceId))
            {
                var error = Bass.LastError;
                if (error != Errors.Already) return false;
            }
            return true;
        }

        public void Start()
        {
            if (IsRecording) return;

            Bass.CurrentRecordingDevice = _deviceId;

            _handle = Bass.RecordStart(44100, 2, BassFlags.RecordPause, _recordProcedure, IntPtr.Zero);

            if (_handle == 0)
                throw new Exception($"Failed to start recording. Error: {Bass.LastError}");

            _dspHandle = Bass.ChannelSetDSP(_handle, _gainDsp, IntPtr.Zero, 1);

            StartNewFile();

            Bass.ChannelPlay(_handle);
            
            _levelTimer.Start();
            _fileRotatorTimer.Start();
            IsRecording = true;
            StartTime = DateTime.Now;
        }

        private void StartNewFile()
        {
            if (_encoderHandle != 0)
            {
                BassEnc.EncodeStop(_encoderHandle);
                _encoderHandle = 0;
            }

            _currentFileDate = DateTime.Now;
            string dateFolder = _currentFileDate.ToString("yyyy-MM-dd");
            string safeStationName = string.Join("_", StationName.Split(Path.GetInvalidFileNameChars()));
            string folderPath = Path.Combine(_settings.RecordingBasePath, dateFolder, safeStationName);
            
            Directory.CreateDirectory(folderPath);

            string fileName = $"{safeStationName}_{_currentFileDate:HH-mm-ss}.mp3";
            _currentFilePath = Path.Combine(folderPath, fileName);

            string lamePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lame.exe");
            string command = $"\"{lamePath}\" -b {_settings.Mp3Bitrate} - \"{_currentFilePath}\"";

            _encoderHandle = BassEnc.EncodeStart(_handle, command, EncodeFlags.AutoFree, null, IntPtr.Zero);

            if (_encoderHandle == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[CRITICAL] Encoder failed: {Bass.LastError}. Cmd: {command}");
            }
        }

        public void StartStreaming(StreamingConfig config)
        {
            _currentConfig = config;
            StopStreaming(false); // No manual stop, just clean up

            IsReconnecting = true;
            IsStreaming = false;

            _streamingClient = new StreamingClient(config, StationName, _handle);
            
            System.Threading.Tasks.Task.Run(() => 
            {
                if (_streamingClient.Connect())
                {
                    IsStreaming = true;
                    IsReconnecting = false;
                }
                else
                {
                    OnClientDisconnected("Connection Failed");
                }
            });
        }

        private void OnClientDisconnected(string reason)
        {
            IsStreaming = false;
            
            if (_streamingClient != null)
            {
                _streamingClient.Dispose();
                _streamingClient = null;
            }

            if (_currentConfig != null)
            {
                IsReconnecting = true;
                _reconnectTimer.Start();
            }
        }

        private void OnReconnectTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            if (_currentConfig != null)
            {
                StartStreaming(_currentConfig);
            }
        }

        public void StopStreaming(bool manualStop = true)
        {
            if (manualStop)
            {
                _currentConfig = null;
                _reconnectTimer.Stop();
            }

            IsReconnecting = false;
            IsStreaming = false;

            if (_streamingClient != null)
            {
                _streamingClient.Dispose();
                _streamingClient = null;
            }
        }

        private void CheckFileRotation(object? sender, ElapsedEventArgs e)
        {
            if (DateTime.Now.Date > _currentFileDate.Date)
            {
                StartNewFile();
            }
        }

        private void UpdateLevels(object? sender, ElapsedEventArgs e)
        {
            if (_handle == 0) return;

            int level = Bass.ChannelGetLevel(_handle);
            if (level != -1)
            {
                int left = level & 0xFFFF;
                int right = (level >> 16) & 0xFFFF;

                LeftLevel = Math.Min(1.0f, (left / 32768f) * _currentGain);
                RightLevel = Math.Min(1.0f, (right / 32768f) * _currentGain);
            }
        }

        public void Stop()
        {
            IsRecording = false;
            _levelTimer.Stop();
            _fileRotatorTimer.Stop();
            _reconnectTimer.Stop();

            if (_encoderHandle != 0)
            {
                BassEnc.EncodeStop(_encoderHandle);
                _encoderHandle = 0;
            }

            if (_handle != 0)
            {
                Bass.ChannelStop(_handle);
                _handle = 0;
            }
            
            StopStreaming(true);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}