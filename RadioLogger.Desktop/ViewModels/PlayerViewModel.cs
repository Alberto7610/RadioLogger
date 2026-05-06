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
using Serilog;

namespace RadioLogger.ViewModels
{
    public partial class PlayerViewModel : ObservableObject, IDisposable
    {
        private static readonly ILogger _log = AppLog.For<PlayerViewModel>();
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
        private AudioDevice _selectedOutputDevice = null!;

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
        private bool _isContinuousPlayback;

        [ObservableProperty]
        private bool _isLoopEnabled;

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

        // LRU cache: evita regenerar waveforms al navegar entre archivos recientes.
        // Capacidad 5 ≈ <100 MB con grabaciones tipicas; un MP3 mono de 1h pesa ~158 MB en float[].
        private readonly LruCache<string, RadioLogger.Services.WaveformData> _waveformCache =
            new LruCache<string, RadioLogger.Services.WaveformData>(5);

        [ObservableProperty]
        private RecordingFile _selectedRecording = null!;

        public ObservableCollection<double> WaveformData { get; } = new ObservableCollection<double>();
        public ObservableCollection<RecordingFile> RecordingsList { get; } = new ObservableCollection<RecordingFile>();
        public ObservableCollection<string> Stations { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> AvailableDates { get; } = new ObservableCollection<string>();

        [ObservableProperty]
        private string _selectedStation = string.Empty;

        [ObservableProperty]
        private string _selectedDate = string.Empty;

        [ObservableProperty]
        private bool _isSortAscending;

        private string _basePath;
        private DateTime _fileStartTime;

        // Pre-loaded next stream for gapless continuous playback
        private int _nextStream;
        private string? _nextFilePath;
        private WaveformData? _nextWaveformData;
        private DateTime _nextFileStartTime;

        public PlayerViewModel(string basePath)
        {
            _basePath = basePath;
            LoadOutputDevices();
            if (!Bass.Init(-1, 44100, DeviceInitFlags.Default, IntPtr.Zero))
            {
                var error = Bass.LastError;
                if (error != Errors.Already)
                    _log.Warning("Bass.Init failed: {Error}", error);
            }
            
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
                App.Current.Dispatcher.BeginInvoke(() => OnPlaybackEnded());
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

            // Pre-load next file when near the end (last 3 seconds) for gapless transition
            if (IsContinuousPlayback && _nextStream == 0 && !IsConcatenatedMode)
            {
                double remaining = TotalDuration - CurrentPosition;
                if (remaining > 0 && remaining < 3.0)
                {
                    PreloadNextFile();
                }
            }

            int level = Bass.ChannelGetLevel(_stream);
            if (level != -1)
            {
                PlaybackLevelL = ((level & 0xFFFF) / 32768.0) * 100;
                PlaybackLevelR = ((level >> 16) / 32768.0) * 100;
            }

            App.Current.Dispatcher.BeginInvoke(() => {
                WaveformData.Add(PlaybackLevelL / 2);
                if (WaveformData.Count > 100) WaveformData.RemoveAt(0);
            });
        }

        private void OnPlaybackEnded()
        {
            if (IsLoopEnabled && _stream != 0)
            {
                // Loop: restart same file
                Bass.ChannelSetPosition(_stream, 0);
                Bass.ChannelPlay(_stream);
                CurrentPosition = 0;
                return;
            }

            if (IsContinuousPlayback && !IsConcatenatedMode)
            {
                // Continuous: switch to next file
                if (AdvanceToNextFile())
                    return;
            }

            // Normal stop
            Stop();
        }

        private bool AdvanceToNextFile()
        {
            var nextRec = GetNextRecording();
            if (nextRec == null) return false;

            // Use pre-loaded stream if available
            if (_nextStream != 0 && _nextFilePath == nextRec.FullPath)
            {
                // Swap streams instantly
                if (_stream != 0) Bass.StreamFree(_stream);
                _stream = _nextStream;
                _nextStream = 0;
                _fileStartTime = _nextFileStartTime;

                CurrentTitle = Path.GetFileName(nextRec.FullPath);
                var len = Bass.ChannelGetLength(_stream);
                TotalDuration = Bass.ChannelBytes2Seconds(_stream, len);
                TimeDisplay = $"00:00:00 / {FormatTime(TotalDuration)}";
                CurrentPosition = 0;

                Bass.ChannelSetAttribute(_stream, ChannelAttribute.Volume, OutputVolume / 100.0);
                Bass.ChannelPlay(_stream);

                // Use pre-loaded waveform
                if (_nextWaveformData != null)
                {
                    _waveformRawData = _nextWaveformData;
                    _waveformCache.Set(nextRec.FullPath, _nextWaveformData);
                    _nextWaveformData = null;
                    WaveformBitmap = WaveformRenderer.RenderRegion(_waveformRawData, 0, 1, 800, 200);
                }
                else
                {
                    _ = GenerateWaveformAsync(nextRec.FullPath);
                }

                // Update selection in list (without triggering OnSelectedRecordingChanged reload)
                _suppressRecordingChange = true;
                SelectedRecording = nextRec;
                _suppressRecordingChange = false;

                _nextFilePath = null;
                return true;
            }

            // No pre-loaded stream — load normally (small gap)
            CleanupPreload();
            _playbackTimer.Stop();
            _suppressRecordingChange = true;
            SelectedRecording = nextRec;
            _suppressRecordingChange = false;
            LoadFile(nextRec.FullPath);
            PlayPause();
            return true;
        }

        private RecordingFile? GetNextRecording()
        {
            if (SelectedRecording == null || RecordingsList.Count == 0) return null;
            int idx = RecordingsList.IndexOf(SelectedRecording);
            if (idx < 0 || idx >= RecordingsList.Count - 1) return null;
            return RecordingsList[idx + 1];
        }

        private void PreloadNextFile()
        {
            var nextRec = GetNextRecording();
            if (nextRec == null) return;
            if (_nextFilePath == nextRec.FullPath) return; // already preloading

            _nextFilePath = nextRec.FullPath;

            // Create stream on background thread (Bass.CreateStream is thread-safe for decode)
            var path = nextRec.FullPath;
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    int stream = Bass.CreateStream(path, 0, 0, BassFlags.Default);
                    if (stream == 0) return;

                    // Parse start time
                    DateTime startTime = default;
                    try
                    {
                        var nameOnly = Path.GetFileNameWithoutExtension(path);
                        var parts = nameOnly.Split('-');
                        if (parts.Length >= 3)
                        {
                            string timeStr = $"{parts[1]}-{parts[2]}";
                            DateTime.TryParseExact(timeStr, "ddMMyy-HHmmss", null, DateTimeStyles.None, out startTime);
                        }
                    }
                    catch { }

                    // Pre-generate waveform
                    var wfData = await WaveformRenderer.ExtractDataAsync(path);

                    // Store for instant swap
                    _nextStream = stream;
                    _nextFileStartTime = startTime;
                    _nextWaveformData = wfData;
                }
                catch { }
            });
        }

        private void CleanupPreload()
        {
            if (_nextStream != 0)
            {
                Bass.StreamFree(_nextStream);
                _nextStream = 0;
            }
            _nextFilePath = null;
            _nextWaveformData = null;
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
                    LoadFile(SelectedRecording.FullPath);
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
                if (_waveformCache.TryGet(path, out var cached))
                {
                    _waveformRawData = cached;
                    var data = cached;
                    _ = App.Current.Dispatcher.BeginInvoke(() =>
                    {
                        WaveformBitmap = RadioLogger.Services.WaveformRenderer.RenderRegion(data, 0, 1, 800, 200);
                    });
                    return;
                }

                var generated = await RadioLogger.Services.WaveformRenderer.ExtractDataAsync(path);
                _waveformRawData = generated;
                if (generated != null) _waveformCache.Set(path, generated);

                _ = App.Current.Dispatcher.BeginInvoke(() =>
                {
                    if (generated != null)
                        WaveformBitmap = RadioLogger.Services.WaveformRenderer.RenderRegion(generated, 0, 1, 800, 200);
                });
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "No se pudo generar waveform de {Path}", path);
            }
            finally
            {
                IsGeneratingVisualization = false;
            }
        }

        // Debounce token for waveform rendering during rapid zoom/resize
        private System.Threading.CancellationTokenSource? _renderCts;

        /// <summary>
        /// Renders waveform for a specific zoom region. Called from code-behind on zoom change.
        /// Runs the heavy SkiaSharp work on a background thread to avoid blocking the UI.
        /// </summary>
        public async void RenderWaveformRegion(double startRatio, double endRatio, int width, int height)
        {
            if (_waveformRawData == null) return;

            // Cancel any pending render
            _renderCts?.Cancel();
            _renderCts = new System.Threading.CancellationTokenSource();
            var token = _renderCts.Token;

            var data = _waveformRawData;
            try
            {
                var bmp = await System.Threading.Tasks.Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    return RadioLogger.Services.WaveformRenderer.RenderRegion(data, startRatio, endRatio, width, height);
                }, token);

                if (!token.IsCancellationRequested && bmp != null)
                    WaveformBitmap = bmp;
            }
            catch (OperationCanceledException) { }
        }

        private bool _suppressRecordingChange;

        partial void OnSelectedRecordingChanged(RecordingFile value)
        {
            if (_suppressRecordingChange) return;
            if (value != null)
            {
                CleanupPreload();
                Stop();
                LoadFile(value.FullPath);
            }
        }

        partial void OnIsSortAscendingChanged(bool value) => _ = LoadRecordingsAsync();

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

        private static readonly string[] ExcludedFolders = { "Logs", "Testigos" };

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
                    string name = Path.GetFileName(sDir);
                    if (ExcludedFolders.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                    Stations.Add(name);
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

            var esMx = new CultureInfo("es-MX");
            var dateStrings = new List<(DateTime date, string display)>();

            try
            {
                // 1. Scan subfolders (new format: dd-MMMM-yyyy)
                foreach (var dir in Directory.GetDirectories(stationPath))
                {
                    string folderName = Path.GetFileName(dir);
                    if (DateTime.TryParseExact(folderName, "dd-MMMM-yyyy", esMx, DateTimeStyles.None, out var dt))
                    {
                        dateStrings.Add((dt, folderName));
                    }
                }

                // 2. Scan loose files (legacy format without subfolders)
                var looseFiles = Directory.GetFiles(stationPath, "*.mp3");
                foreach (var file in looseFiles)
                {
                    var nameOnly = Path.GetFileNameWithoutExtension(file);
                    var parts = nameOnly.Split('-');
                    if (parts.Length >= 2 && DateTime.TryParseExact(parts[1], "ddMMyy", null, DateTimeStyles.None, out var dt))
                    {
                        string display = dt.ToString("dd-MMMM-yyyy", esMx);
                        if (!dateStrings.Any(d => d.display == display))
                            dateStrings.Add((dt, display));
                    }
                }
            }
            catch { }

            foreach (var d in dateStrings.OrderByDescending(x => x.date))
                AvailableDates.Add(d.display);

            if (AvailableDates.Any()) SelectedDate = AvailableDates.First();
            else _ = LoadRecordingsAsync();
        }

        partial void OnSelectedDateChanged(string value) => _ = LoadRecordingsAsync();

        private async System.Threading.Tasks.Task LoadRecordingsAsync()
        {
            RecordingsList.Clear();
            if (string.IsNullOrEmpty(SelectedStation) || string.IsNullOrEmpty(SelectedDate)) return;

            string stationPath = Path.Combine(_basePath, "RadioLogger", SelectedStation);
            string selectedDate = SelectedDate;
            bool sortAsc = IsSortAscending;

            // Run all disk I/O on background thread
            var recordings = await System.Threading.Tasks.Task.Run(() =>
            {
                if (!Directory.Exists(stationPath)) return Array.Empty<RecordingFile>();

                var esMx = new CultureInfo("es-MX");
                var allFiles = new List<FileInfo>();

                try
                {
                    string dateSubFolder = Path.Combine(stationPath, selectedDate);
                    if (Directory.Exists(dateSubFolder))
                        allFiles.AddRange(new DirectoryInfo(dateSubFolder).GetFiles("*.mp3"));

                    if (DateTime.TryParseExact(selectedDate, "dd-MMMM-yyyy", esMx, DateTimeStyles.None, out var dt))
                    {
                        string dateStr = dt.ToString("ddMMyy");
                        var loose = new DirectoryInfo(stationPath).GetFiles("*.mp3")
                            .Where(f => f.Name.Contains($"-{dateStr}-"));
                        allFiles.AddRange(loose);
                    }
                }
                catch { }

                var sorted = sortAsc
                    ? allFiles.OrderBy(f => f.Name)
                    : allFiles.OrderByDescending(f => f.Name);

                int bitrate = 128;
                return sorted.Select(f =>
                {
                    double estimatedSeconds = (f.Length * 8.0) / (bitrate * 1000.0);
                    return new RecordingFile(f, TimeSpan.FromSeconds(estimatedSeconds));
                }).ToArray();
            });

            // Batch-add to UI on dispatcher
            foreach (var rec in recordings)
                RecordingsList.Add(rec);
        }

        [RelayCommand]
        public void ToggleSortOrder() => IsSortAscending = !IsSortAscending;

        [RelayCommand] public void ToggleMute() => IsMuted = !IsMuted;
        [RelayCommand] public void ToggleContinuousPlayback() { IsContinuousPlayback = !IsContinuousPlayback; if (!IsContinuousPlayback) CleanupPreload(); }
        [RelayCommand] public void ToggleLoop() { IsLoopEnabled = !IsLoopEnabled; }

        /// <summary>
        /// Refresca la lista de archivos manteniendo la fecha seleccionada.
        /// </summary>
        public void RefreshFileList()
        {
            var currentDate = SelectedDate;
            RefreshDates();
            if (!string.IsNullOrEmpty(currentDate) && AvailableDates.Contains(currentDate))
                SelectedDate = currentDate;
        }

        // ─── CONCATENATION ──────────────────────────────────────

        public async System.Threading.Tasks.Task ConcatenateFilesAsync(IList<RecordingFile> selectedFiles)
        {
            if (selectedFiles == null || selectedFiles.Count < 2) return;

            Stop();
            CleanupConcatenation();

            IsConcatenating = true;
            ConcatenateStatus = $"Uniendo {selectedFiles.Count} archivos...";

            try
            {
                var sorted = selectedFiles.OrderBy(f => f.FileName).Select(f => f.FullPath).ToList();

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
                    _ = App.Current.Dispatcher.BeginInvoke(() =>
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
            ConcatenateStatus = "Exportando MP3... 0%";
            IsConcatenating = true;

            // Progress<T> captura el SynchronizationContext actual (UI), por lo que
            // ConcatenateStatus se actualiza en el hilo correcto sin Dispatcher manual.
            var progress = new Progress<double>(pct =>
            {
                ConcatenateStatus = $"Exportando MP3... {pct:F0}%";
            });

            try
            {
                bool ok = await AudioConcatenator.ExportToMp3Async(audio, outputPath, progress);
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
            CleanupPreload();
            CleanupConcatenation();
            if (_stream != 0) Bass.StreamFree(_stream);
            _playbackTimer.Dispose();
        }
    }

    /// <summary>
    /// Wrapper for a recording file with pre-calculated duration.
    /// </summary>
    public class RecordingFile
    {
        public string FileName { get; }
        public string FullPath { get; }
        public DateTime LastWriteTime { get; }
        public long SizeBytes { get; }
        public TimeSpan Duration { get; }

        public string DurationText => Duration.TotalHours >= 1
            ? $"{(int)Duration.TotalHours:D2}:{Duration.Minutes:D2}:{Duration.Seconds:D2}"
            : $"{Duration.Minutes:D2}:{Duration.Seconds:D2}";

        public string TimeText { get; }

        public RecordingFile(FileInfo fi, TimeSpan duration)
        {
            FileName = fi.Name;
            FullPath = fi.FullName;
            LastWriteTime = fi.LastWriteTime;
            SizeBytes = fi.Length;
            Duration = duration;

            // Extract broadcast time from filename: STATION-ddMMyy-HHmmss.mp3
            var parts = Path.GetFileNameWithoutExtension(fi.Name).Split('-');
            if (parts.Length >= 3 && DateTime.TryParseExact($"{parts[1]}-{parts[2]}", "ddMMyy-HHmmss", null, DateTimeStyles.None, out var dt))
                TimeText = dt.ToString("HH:mm:ss");
            else
                TimeText = fi.LastWriteTime.ToString("HH:mm:ss");
        }
    }
}
