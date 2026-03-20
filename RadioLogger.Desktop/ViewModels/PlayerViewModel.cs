using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ManagedBass;
using RadioLogger.Models;
using RadioLogger.Helpers;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Timers;
using System.Globalization;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using RadioLogger.Services;

namespace RadioLogger.ViewModels
{
    public partial class PlayerViewModel : ObservableObject, IDisposable
    {
        private int _stream;
        private readonly System.Timers.Timer _playbackTimer;

        // Concatenation state
        private ConcatenatedAudio? _concatenatedAudio;
        private string? _concatenatedTempFile;

        [ObservableProperty]
        private bool _isConcatenating;

        [ObservableProperty]
        private bool _isConcatenatedMode;

        [ObservableProperty]
        private string _concatenateStatus = "";
        
        public ObservableCollection<AudioDevice> OutputDevices { get; } = new ObservableCollection<AudioDevice>();
        
        [ObservableProperty]
        private AudioDevice _selectedOutputDevice;

        [ObservableProperty]
        private bool _isMasterVolumeControl;

        [ObservableProperty]
        private double _outputVolume = 100;

        [ObservableProperty]
        private bool _isMuted;

        private double _preMuteVolume = 100;
        private double _lastAppVolume = 100;

        partial void OnIsMasterVolumeControlChanged(bool value)
        {
            if (value)
            {
                _lastAppVolume = OutputVolume;
                OutputVolume = SystemAudioHelper.GetMasterVolume();
            }
            else
            {
                OutputVolume = _lastAppVolume;
                if (_stream != 0) Bass.ChannelSetAttribute(_stream, ChannelAttribute.Volume, (float)(_lastAppVolume / 100.0));
            }
        }

        partial void OnOutputVolumeChanged(double value)
        {
            if (_isMuted && value > 0) IsMuted = false;
            
            if (IsMasterVolumeControl)
            {
                SystemAudioHelper.SetMasterVolume((float)value);
            }
            else
            {
                if (_stream != 0) Bass.ChannelSetAttribute(_stream, ChannelAttribute.Volume, (float)(value / 100.0));
            }
        }

        partial void OnIsMutedChanged(bool value)
        {
            if (value) { _preMuteVolume = OutputVolume; OutputVolume = 0; }
            else { OutputVolume = _preMuteVolume > 0 ? _preMuteVolume : 50; }
        }

        [ObservableProperty]
        private string _currentTitle = "No hay archivo cargado";

        [ObservableProperty]
        private double _currentPosition;

        [ObservableProperty]
        private double _totalDuration;

        [ObservableProperty]
        private string _timeDisplay = "00:00:00 / 00:00:00";

        [ObservableProperty]
        private string _realTimeDisplay = "--:--:--";

        [ObservableProperty]
        private bool _isPlaying;

        [ObservableProperty]
        private double _playbackLevelL;

        [ObservableProperty]
        private double _playbackLevelR;

        [ObservableProperty]
        private bool _isGeneratingVisualization;

        [ObservableProperty]
        private WriteableBitmap? _waveformBitmap;

        // Raw waveform data for SkiaSharp zoom rendering
        private RadioLogger.Services.WaveformData? _waveformRawData;

        [ObservableProperty]
        private FileSystemInfo _selectedRecording;

        public ObservableCollection<double> WaveformData { get; } = new ObservableCollection<double>();
        public ObservableCollection<FileSystemInfo> RecordingsList { get; } = new ObservableCollection<FileSystemInfo>();
        public ObservableCollection<string> Stations { get; } = new ObservableCollection<string>();
        public ObservableCollection<DateTime> AvailableDates { get; } = new ObservableCollection<DateTime>();

        [ObservableProperty]
        private string _selectedStation;

        [ObservableProperty]
        private DateTime? _selectedDate;

        private string _basePath;
        private DateTime _fileStartTime;

        public PlayerViewModel(string basePath)
        {
            _basePath = basePath;
            LoadOutputDevices();
            if (!Bass.Init(-1, 44100, DeviceInitFlags.Default, IntPtr.Zero)) { }
            
            RefreshStations();

            _playbackTimer = new System.Timers.Timer(100);
            _playbackTimer.Elapsed += (s, e) => UpdatePosition();
        }

        private void LoadOutputDevices()
        {
            OutputDevices.Clear();
            for (int i = 1; i < Bass.DeviceCount; i++)
            {
                var info = Bass.GetDeviceInfo(i);
                if (info.IsEnabled && !info.IsLoopback)
                {
                    OutputDevices.Add(new AudioDevice { Id = i, Name = info.Name, Driver = info.Driver });
                }
            }
            if (OutputDevices.Any()) SelectedOutputDevice = OutputDevices.First();
        }

        private bool _isSeeking;

        public void BeginSeek() => _isSeeking = true;

        public void EndSeek()
        {
            if (_stream != 0)
            {
                long bytePos = Bass.ChannelSeconds2Bytes(_stream, CurrentPosition);
                Bass.ChannelSetPosition(_stream, bytePos);
            }
            _isSeeking = false;
        }

        partial void OnCurrentPositionChanged(double value)
        {
            if (_isSeeking && _stream != 0)
            {
                TimeDisplay = $"{FormatTime(value)} / {FormatTime(TotalDuration)}";
                if (_fileStartTime != default)
                {
                    RealTimeDisplay = _fileStartTime.AddSeconds(value).ToString("HH:mm:ss");
                }
            }
        }

        private void UpdatePosition()
        {
            if (_stream == 0 || !IsPlaying) return;
            if (_isSeeking) return;

            // Detect end of playback
            var state = Bass.ChannelIsActive(_stream);
            if (state == PlaybackState.Stopped)
            {
                App.Current.Dispatcher.Invoke(() => Stop());
                return;
            }

            var pos = Bass.ChannelGetPosition(_stream);
            var len = Bass.ChannelGetLength(_stream);

            CurrentPosition = Bass.ChannelBytes2Seconds(_stream, pos);
            TotalDuration = Bass.ChannelBytes2Seconds(_stream, len);

            TimeDisplay = $"{FormatTime(CurrentPosition)} / {FormatTime(TotalDuration)}";

            if (_fileStartTime != default)
            {
                var realTime = _fileStartTime.AddSeconds(CurrentPosition);
                RealTimeDisplay = realTime.ToString("HH:mm:ss");
            }

            int level = Bass.ChannelGetLevel(_stream);
            if (level != -1)
            {
                PlaybackLevelL = ((level & 0xFFFF) / 32768.0) * 100;
                PlaybackLevelR = ((level >> 16) / 32768.0) * 100;
            }

            App.Current.Dispatcher.Invoke(() => {
                WaveformData.Add(PlaybackLevelL / 2);
                if (WaveformData.Count > 100) WaveformData.RemoveAt(0);
            });
        }

        private string FormatTime(double seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            return $"{(int)t.TotalHours:D2}:{t.Minutes:D2}:{t.Seconds:D2}";
        }

        [RelayCommand]
        public void PlayPause()
        {
            if (IsPlaying)
            {
                Bass.ChannelPause(_stream);
                IsPlaying = false;
                _playbackTimer.Stop();
            }
            else
            {
                if (_stream != 0)
                {
                    Bass.ChannelPlay(_stream);
                    IsPlaying = true;
                    _playbackTimer.Start();
                }
                else if (SelectedRecording != null)
                {
                    LoadFile(SelectedRecording.FullName);
                }
            }
        }

        private void LoadFile(string path)
        {
            if (_stream != 0) { Bass.StreamFree(_stream); _stream = 0; }

            // New Filename format: STATION-ddMMyy-HHmmss.mp3
            try {
                var nameOnly = Path.GetFileNameWithoutExtension(path);
                var parts = nameOnly.Split('-');
                if (parts.Length >= 3)
                {
                    string timeStr = $"{parts[1]}-{parts[2]}";
                    if (DateTime.TryParseExact(timeStr, "ddMMyy-HHmmss", null, DateTimeStyles.None, out var dt))
                    {
                        _fileStartTime = dt;
                    }
                }
            } catch { }

            _stream = Bass.CreateStream(path, 0, 0, BassFlags.Default);
            if (_stream != 0)
            {
                CurrentTitle = Path.GetFileName(path);
                var len = Bass.ChannelGetLength(_stream);
                TotalDuration = Bass.ChannelBytes2Seconds(_stream, len);
                TimeDisplay = $"00:00:00 / {FormatTime(TotalDuration)}";

                Bass.ChannelSetAttribute(_stream, ChannelAttribute.Volume, OutputVolume / 100.0);
                CurrentPosition = 0;
                IsPlaying = false;

                // Generate waveform in background
                _ = GenerateWaveformAsync(path);
            }
        }

        private async System.Threading.Tasks.Task GenerateWaveformAsync(string path)
        {
            IsGeneratingVisualization = true;
            try
            {
                _waveformRawData = await RadioLogger.Services.WaveformRenderer.ExtractDataAsync(path);

                App.Current.Dispatcher.Invoke(() =>
                {
                    WaveformBitmap = RadioLogger.Services.WaveformRenderer.RenderRegion(_waveformRawData, 0, 1, 800, 200);
                });
            }
            catch { }
            finally
            {
                IsGeneratingVisualization = false;
            }
        }

        /// <summary>
        /// Renders waveform for a specific zoom region. Called from code-behind on zoom change.
        /// </summary>
        public void RenderWaveformRegion(double startRatio, double endRatio, int width, int height)
        {
            if (_waveformRawData == null) return;
            var bmp = RadioLogger.Services.WaveformRenderer.RenderRegion(_waveformRawData, startRatio, endRatio, width, height);
            if (bmp != null)
            {
                WaveformBitmap = bmp;
            }
        }

        partial void OnSelectedRecordingChanged(FileSystemInfo value)
        {
            if (value != null)
            {
                Stop();
                LoadFile(value.FullName);
            }
        }

        [RelayCommand]
        public void Stop()
        {
            if (_stream != 0)
            {
                Bass.ChannelStop(_stream);
                Bass.ChannelSetPosition(_stream, 0);
            }
            IsPlaying = false;
            _playbackTimer.Stop();
            CurrentPosition = 0;
            PlaybackLevelL = 0;
            PlaybackLevelR = 0;
            TimeDisplay = $"00:00:00 / {FormatTime(TotalDuration)}";
            if (_fileStartTime != default)
                RealTimeDisplay = _fileStartTime.ToString("HH:mm:ss");
        }

        [RelayCommand]
        public void Rewind()
        {
            if (_stream != 0)
            {
                var pos = Bass.ChannelGetPosition(_stream);
                double seconds = Bass.ChannelBytes2Seconds(_stream, pos);
                Bass.ChannelSetPosition(_stream, Bass.ChannelSeconds2Bytes(_stream, Math.Max(0, seconds - 10)));
            }
        }

        private void RefreshStations()
        {
            Stations.Clear();
            string rootPath = Path.Combine(_basePath, "RadioLogger");
            if (!Directory.Exists(rootPath)) return;

            try
            {
                var sDirs = Directory.GetDirectories(rootPath);
                foreach (var sDir in sDirs)
                {
                    Stations.Add(Path.GetFileName(sDir));
                }
            }
            catch { }

            if (Stations.Any()) SelectedStation = Stations.First();
        }

        partial void OnSelectedStationChanged(string value) => RefreshDates();

        private void RefreshDates()
        {
            AvailableDates.Clear();
            if (string.IsNullOrEmpty(SelectedStation)) return;

            string stationPath = Path.Combine(_basePath, "RadioLogger", SelectedStation);
            if (!Directory.Exists(stationPath)) return;

            var dates = new HashSet<DateTime>();
            try
            {
                var files = Directory.GetFiles(stationPath, "*.mp3");
                foreach (var file in files)
                {
                    var nameOnly = Path.GetFileNameWithoutExtension(file);
                    var parts = nameOnly.Split('-');
                    if (parts.Length >= 2 && DateTime.TryParseExact(parts[1], "ddMMyy", null, DateTimeStyles.None, out var dt))
                    {
                        dates.Add(dt.Date);
                    }
                }
            }
            catch { }

            foreach (var d in dates.OrderByDescending(x => x)) AvailableDates.Add(d);
            if (AvailableDates.Any()) SelectedDate = AvailableDates.First();
            else LoadRecordings(); 
        }

        partial void OnSelectedDateChanged(DateTime? value) => LoadRecordings();

        private void LoadRecordings()
        {
            RecordingsList.Clear();
            if (string.IsNullOrEmpty(SelectedStation) || !SelectedDate.HasValue) return;

            string stationPath = Path.Combine(_basePath, "RadioLogger", SelectedStation);
            if (!Directory.Exists(stationPath)) return;

            try {
                var dateStr = SelectedDate.Value.ToString("ddMMyy");
                var files = new DirectoryInfo(stationPath).GetFiles("*.mp3")
                    .Where(f => f.Name.Contains($"-{dateStr}-"))
                    .OrderByDescending(f => f.LastWriteTime); 
                foreach (var f in files) RecordingsList.Add(f);
            } catch { }
        }

        [RelayCommand] public void ToggleMute() => IsMuted = !IsMuted;

        /// <summary>
        /// Refresca la lista de archivos manteniendo la fecha seleccionada.
        /// </summary>
        public void RefreshFileList()
        {
            var currentDate = SelectedDate;
            RefreshDates();
            // Restaurar la fecha si sigue existiendo
            if (currentDate.HasValue && AvailableDates.Contains(currentDate.Value))
                SelectedDate = currentDate.Value;
        }

        // ─── CONCATENATION ──────────────────────────────────────

        public async System.Threading.Tasks.Task ConcatenateFilesAsync(IList<FileSystemInfo> selectedFiles)
        {
            if (selectedFiles == null || selectedFiles.Count < 2) return;

            Stop();
            CleanupConcatenation();

            IsConcatenating = true;
            ConcatenateStatus = $"Uniendo {selectedFiles.Count} archivos...";

            try
            {
                var sorted = selectedFiles.OrderBy(f => f.Name).Select(f => f.FullName).ToList();

                var result = await AudioConcatenator.ConcatenateAsync(sorted);
                if (result == null)
                {
                    ConcatenateStatus = "Error al unir archivos";
                    return;
                }

                _concatenatedAudio?.Dispose();
                _concatenatedAudio = result;

                // Write temp WAV (IEEE float) for full BASS playback support (seek, length, etc.)
                ConcatenateStatus = "Preparando audio...";
                _concatenatedTempFile = await System.Threading.Tasks.Task.Run(() => WriteTempWav(result));

                // Load into the main player stream
                if (_stream != 0) { Bass.StreamFree(_stream); _stream = 0; }
                _stream = Bass.CreateStream(_concatenatedTempFile, 0, 0, BassFlags.Float);
                if (_stream == 0)
                {
                    ConcatenateStatus = "Error al cargar audio unido";
                    return;
                }

                Bass.ChannelSetAttribute(_stream, ChannelAttribute.Volume, (float)(OutputVolume / 100.0));
                var len = Bass.ChannelGetLength(_stream);
                TotalDuration = Bass.ChannelBytes2Seconds(_stream, len);
                CurrentPosition = 0;
                IsPlaying = false;
                _fileStartTime = default;
                RealTimeDisplay = "--:--:--";

                // Try to get real start time from first source file
                try
                {
                    var firstName = Path.GetFileNameWithoutExtension(sorted.First());
                    var parts = firstName.Split('-');
                    if (parts.Length >= 3)
                    {
                        string timeStr = $"{parts[1]}-{parts[2]}";
                        if (DateTime.TryParseExact(timeStr, "ddMMyy-HHmmss", null, DateTimeStyles.None, out var dt))
                            _fileStartTime = dt;
                    }
                }
                catch { }

                CurrentTitle = $"{selectedFiles.Count} archivos unidos ({FormatTime(TotalDuration)})";
                TimeDisplay = $"00:00:00 / {FormatTime(TotalDuration)}";

                // Generate waveform from PCM data
                ConcatenateStatus = "Generando forma de onda...";
                var wfData = await Services.WaveformRenderer.ExtractDataFromPcmAsync(
                    result.PcmData, result.SampleRate, result.Channels);

                if (wfData != null)
                {
                    _waveformRawData = wfData;
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        WaveformBitmap = Services.WaveformRenderer.RenderRegion(wfData, 0, 1, 800, 200);
                    });
                }

                IsConcatenatedMode = true;
                ConcatenateStatus = $"{selectedFiles.Count} archivos unidos — {FormatTime(TotalDuration)}";
            }
            catch (Exception ex)
            {
                ConcatenateStatus = $"Error: {ex.Message}";
            }
            finally
            {
                IsConcatenating = false;
            }
        }

        private static string WriteTempWav(ConcatenatedAudio audio)
        {
            string path = Path.Combine(Path.GetTempPath(), $"radiologger_{Guid.NewGuid():N}.wav");

            using var fs = new FileStream(path, FileMode.Create);
            using var bw = new BinaryWriter(fs);

            int bitsPerSample = 32;
            int blockAlign = audio.Channels * (bitsPerSample / 8);
            int byteRate = audio.SampleRate * blockAlign;
            int dataBytes = audio.PcmData.Length * sizeof(float);

            // RIFF header
            bw.Write(new[] { 'R', 'I', 'F', 'F' });
            bw.Write(36 + dataBytes);
            bw.Write(new[] { 'W', 'A', 'V', 'E' });

            // fmt chunk — IEEE float (format tag 3)
            bw.Write(new[] { 'f', 'm', 't', ' ' });
            bw.Write(16);
            bw.Write((short)3); // IEEE float
            bw.Write((short)audio.Channels);
            bw.Write(audio.SampleRate);
            bw.Write(byteRate);
            bw.Write((short)blockAlign);
            bw.Write((short)bitsPerSample);

            // data chunk
            bw.Write(new[] { 'd', 'a', 't', 'a' });
            bw.Write(dataBytes);

            byte[] buf = new byte[dataBytes];
            Buffer.BlockCopy(audio.PcmData, 0, buf, 0, buf.Length);
            bw.Write(buf);

            return path;
        }

        [RelayCommand]
        public void ExportConcatenation()
        {
            if (_concatenatedAudio == null)
            {
                ConcatenateStatus = "No hay audio para exportar";
                return;
            }

            // Pause if playing
            if (IsPlaying) PlayPause();

            string suggestedName = BuildConcatFilename();
            string initialDir = "";
            if (!string.IsNullOrEmpty(SelectedStation))
            {
                initialDir = Path.Combine(_basePath, "RadioLogger", SelectedStation);
                if (!Directory.Exists(initialDir)) initialDir = "";
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "MP3 Audio|*.mp3",
                FileName = suggestedName,
                DefaultExt = ".mp3"
            };
            if (!string.IsNullOrEmpty(initialDir))
                dialog.InitialDirectory = initialDir;

            if (dialog.ShowDialog() != true) return;

            // Capture reference before async work
            var audioToExport = _concatenatedAudio;
            string outputPath = dialog.FileName;

            _ = DoExportAsync(audioToExport, outputPath);
        }

        private async System.Threading.Tasks.Task DoExportAsync(ConcatenatedAudio audio, string outputPath)
        {
            ConcatenateStatus = "Exportando MP3...";
            IsConcatenating = true;

            try
            {
                bool ok = await AudioConcatenator.ExportToMp3Async(audio, outputPath);
                ConcatenateStatus = ok
                    ? $"Exportado: {Path.GetFileName(outputPath)}"
                    : "Error al exportar MP3";

                if (ok)
                {
                    string savedDir = Path.GetDirectoryName(outputPath) ?? "";
                    string stationDir = !string.IsNullOrEmpty(SelectedStation)
                        ? Path.Combine(_basePath, "RadioLogger", SelectedStation) : "";

                    if (!string.IsNullOrEmpty(stationDir) &&
                        string.Equals(Path.GetFullPath(savedDir), Path.GetFullPath(stationDir), StringComparison.OrdinalIgnoreCase))
                    {
                        RefreshDates();
                    }
                }
            }
            catch (Exception ex)
            {
                ConcatenateStatus = $"Error: {ex.Message}";
            }
            finally
            {
                IsConcatenating = false;
            }
        }

        [RelayCommand]
        public void DiscardConcatenation()
        {
            Stop();
            if (_stream != 0) { Bass.StreamFree(_stream); _stream = 0; }
            CleanupConcatenation();

            IsConcatenatedMode = false;
            ConcatenateStatus = "";
            CurrentTitle = "No hay archivo cargado";
            TotalDuration = 0;
            CurrentPosition = 0;
            TimeDisplay = "00:00:00 / 00:00:00";
            RealTimeDisplay = "--:--:--";
            WaveformBitmap = null;
            _waveformRawData = null;
        }

        private void CleanupConcatenation()
        {
            _concatenatedAudio?.Dispose();
            _concatenatedAudio = null;

            if (_concatenatedTempFile != null)
            {
                try { File.Delete(_concatenatedTempFile); } catch { }
                _concatenatedTempFile = null;
            }
        }

        private string BuildConcatFilename()
        {
            if (_concatenatedAudio == null || _concatenatedAudio.SourceFiles.Count == 0)
                return "concatenado.mp3";

            try
            {
                var first = Path.GetFileNameWithoutExtension(_concatenatedAudio.SourceFiles.First());
                var last = Path.GetFileNameWithoutExtension(_concatenatedAudio.SourceFiles.Last());

                var firstParts = first.Split('-');
                var lastParts = last.Split('-');

                if (firstParts.Length >= 3 && lastParts.Length >= 3)
                {
                    string station = firstParts[0];
                    string date = firstParts[1];
                    string startTime = firstParts[2];
                    string endTime = lastParts[2];
                    return $"{station}-{date}-{startTime}_{endTime}.mp3";
                }
            }
            catch { }

            return "concatenado.mp3";
        }

        public void Dispose()
        {
            Stop();
            CleanupConcatenation();
            if (_stream != 0) Bass.StreamFree(_stream);
            _playbackTimer.Dispose();
        }
    }
}
