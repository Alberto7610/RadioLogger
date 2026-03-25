using ManagedBass;
using ManagedBass.Enc;
using RadioLogger.Models;
using Serilog;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Timers;

namespace RadioLogger.Services
{
    public class AudioChannel : IDisposable
    {
        private static readonly ILogger _log = AppLog.For<AudioChannel>();

        private int _handle;
        private int _deviceId;
        private readonly AppSettings _settings;
        private readonly System.Timers.Timer _levelTimer;
        private readonly System.Timers.Timer _fileRotatorTimer;
        private readonly System.Timers.Timer _reconnectTimer;

        private bool _isClosing = false;
        private int _encoderHandle;
        private EncodeProcedure _encodeProcedure;
        private FileStream? _currentFileStream;
        private object _fileLock = new object();

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
        public bool RecordToFile { get; set; } = true;

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
                        RotateFile();
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
            _encodeProcedure = new EncodeProcedure(EncoderCallback);

            _levelTimer = new System.Timers.Timer(50);
            _levelTimer.Elapsed += UpdateLevels;

            _fileRotatorTimer = new System.Timers.Timer(1000);
            _fileRotatorTimer.Elapsed += CheckFileRotation;

            _reconnectTimer = new System.Timers.Timer(10000); // 10s retry
            _reconnectTimer.AutoReset = false;
            _reconnectTimer.Elapsed += OnReconnectTimerElapsed;
        }

        private void EncoderCallback(int handle, int channel, IntPtr buffer, int length, IntPtr user)
        {
            if (_isClosing) return;

            lock (_fileLock)
            {
                if (_currentFileStream != null && length > 0)
                {
                    try
                    {
                        byte[] data = new byte[length];
                        Marshal.Copy(buffer, data, 0, length);
                        _currentFileStream.Write(data, 0, length);
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Error escribiendo datos al archivo MP3");
                    }
                }
            }
        }

        public void SetGain(float gain)
        {
            _currentGain = gain;
        }

        private unsafe void GainCallback(int handle, int channel, IntPtr buffer, int length, IntPtr user)
        {
            try
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
            catch (Exception ex)
            {
                _log.Error(ex, "Error en GainCallback");
            }
        }

        private bool RecordingCallback(int handle, IntPtr buffer, int length, IntPtr user)
        {
            try
            {
                return !_isClosing;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Error en RecordingCallback");
                return true;
            }
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
            _isClosing = false;

            Bass.CurrentRecordingDevice = _deviceId;

            _handle = Bass.RecordStart(44100, 2, BassFlags.RecordPause, _recordProcedure, IntPtr.Zero);

            if (_handle == 0)
                throw new Exception($"Failed to start recording. Error: {Bass.LastError}");

            _dspHandle = Bass.ChannelSetDSP(_handle, _gainDsp, IntPtr.Zero, 1);

            if (RecordToFile)
            {
                StartEncoder();
                _fileRotatorTimer.Start();
            }

            Bass.ChannelPlay(_handle);

            _levelTimer.Start();
            IsRecording = true;
            StartTime = DateTime.Now;
        }

        private void StartEncoder()
        {
            string lamePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lame.exe");
            string command = $"\"{lamePath}\" -b {_settings.Mp3Bitrate} - -";

            OpenNewFile();

            _encoderHandle = BassEnc.EncodeStart(_handle, command, EncodeFlags.AutoFree, _encodeProcedure, IntPtr.Zero);
        }

        private void OpenNewFile()
        {
            lock (_fileLock)
            {
                _currentFileDate = DateTime.Now;

                // Sanitize station name: remove invalid chars, path traversal, and clamp length
                string safeStationName = string.Join("_", StationName.Split(Path.GetInvalidFileNameChars()));
                safeStationName = safeStationName.Replace("..", "").Replace("/", "").Replace("\\", "");
                safeStationName = Path.GetFileName(safeStationName); // strip any remaining path components
                if (string.IsNullOrWhiteSpace(safeStationName)) safeStationName = "Unknown";
                if (safeStationName.Length > 100) safeStationName = safeStationName[..100];

                // Structure: Root \ RadioLogger \ StationName \ dd-MMMM-yyyy
                string dateFolder = _currentFileDate.ToString("dd-MMMM-yyyy", new System.Globalization.CultureInfo("es-MX"));
                string folderPath = Path.Combine(_settings.RecordingBasePath, "RadioLogger", safeStationName, dateFolder);

                // Verify the resolved path is within the expected base directory
                string fullPath = Path.GetFullPath(folderPath);
                string baseFull = Path.GetFullPath(Path.Combine(_settings.RecordingBasePath, "RadioLogger"));
                if (!fullPath.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase))
                {
                    _log.Warning("SEGURIDAD: Ruta sospechosa bloqueada: {Path}", fullPath);
                    return;
                }

                Directory.CreateDirectory(folderPath);

                // Filename format: STATION-ddMMyy-HHmmss.mp3
                string fileName = $"{safeStationName}-{_currentFileDate:ddMMyy-HHmmss}.mp3";
                _currentFilePath = Path.Combine(folderPath, fileName);

                _currentFileStream = new FileStream(_currentFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
                _log.Information("Nuevo archivo: {FileName} ({Station})", fileName, safeStationName);
            }
        }

        private void RotateFile()
        {
            lock (_fileLock)
            {
                if (_currentFileStream != null)
                {
                    _currentFileStream.Flush();
                    _currentFileStream.Close();
                    _currentFileStream.Dispose();
                    _currentFileStream = null;
                }
                OpenNewFile();
            }
        }

        public void StartStreaming(StreamingConfig config)
        {
            _currentConfig = config;
            StopStreaming(false);

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

        public void UpdateSettings(AppSettings settings)
        {
            _settings.SegmentDurationMinutes = settings.SegmentDurationMinutes;
            _settings.Mp3Bitrate = settings.Mp3Bitrate;
            _settings.RecordingBasePath = settings.RecordingBasePath;
            _settings.StartHour = settings.StartHour;
            _settings.EndHour = settings.EndHour;
        }

        private void CheckFileRotation(object? sender, ElapsedEventArgs e)
        {
            int intervalMinutes = Math.Max(1, _settings.SegmentDurationMinutes);
            DateTime now = DateTime.Now;
            bool shouldRotate = false;

            lock (_fileLock)
            {
                if (intervalMinutes >= 60)
                {
                    int hours = intervalMinutes / 60;
                    if (now.Hour != _currentFileDate.Hour && now.Hour % hours == 0)
                    {
                        shouldRotate = true;
                    }
                }
                else
                {
                    if (now.Minute != _currentFileDate.Minute && now.Minute % intervalMinutes == 0)
                    {
                        shouldRotate = true;
                    }
                }

                if (now.Date > _currentFileDate.Date) shouldRotate = true;
            }

            if (shouldRotate)
            {
                RotateFile();
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
            if (_isClosing) return;
            _isClosing = true;

            IsRecording = false;
            _levelTimer.Stop();
            _fileRotatorTimer.Stop();
            _reconnectTimer.Stop();

            StopStreaming(true);

            lock (_fileLock)
            {
                if (_encoderHandle != 0)
                {
                    BassEnc.EncodeStop(_encoderHandle);
                    _encoderHandle = 0;
                }

                if (_currentFileStream != null)
                {
                    try
                    {
                        _currentFileStream.Flush();
                        _currentFileStream.Close();
                        _currentFileStream.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Error cerrando archivo de grabación");
                    }
                    finally { _currentFileStream = null; }
                }
            }

            if (_handle != 0)
            {
                if (_dspHandle != 0)
                {
                    Bass.ChannelRemoveDSP(_handle, _dspHandle);
                    _dspHandle = 0;
                }
                Bass.ChannelStop(_handle);
                _handle = 0;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
