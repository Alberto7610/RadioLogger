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

namespace RadioLogger.ViewModels
{
    public partial class PlayerViewModel : ObservableObject, IDisposable
    {
        private int _stream;
        private readonly System.Timers.Timer _playbackTimer;
        
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

        private void UpdatePosition()
        {
            if (_stream == 0 || !IsPlaying) return;

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
                PlaybackLevelL = (level & 0xFFFF) / 32768.0;
                PlaybackLevelR = (level >> 16) / 32768.0;
            }

            App.Current.Dispatcher.Invoke(() => {
                WaveformData.Add(PlaybackLevelL * 50);
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
                    // parts[1] = ddMMyy, parts[2] = HHmmss
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
                Bass.ChannelSetAttribute(_stream, ChannelAttribute.Volume, OutputVolume / 100.0);
                Bass.ChannelPlay(_stream);
                IsPlaying = true;
                _playbackTimer.Start();
            }
        }

        [RelayCommand]
        public void Stop()
        {
            if (_stream != 0)
            {
                Bass.ChannelStop(_stream);
                Bass.ChannelSetPosition(_stream, 0);
                IsPlaying = false;
                _playbackTimer.Stop();
                CurrentPosition = 0;
            }
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

        public void Dispose() { Stop(); if (_stream != 0) Bass.StreamFree(_stream); _playbackTimer.Dispose(); }
    }
}
