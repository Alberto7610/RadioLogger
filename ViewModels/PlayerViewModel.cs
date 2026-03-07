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
                // Switch to Master: Save app volume, load System Volume
                _lastAppVolume = OutputVolume;
                OutputVolume = SystemAudioHelper.GetMasterVolume();
            }
            else
            {
                // Switch to App: Load saved app volume
                OutputVolume = _lastAppVolume;
                // Ensure BASS stream is synced
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

        private void RefreshStations()
        {
            Stations.Clear();
            if (!Directory.Exists(_basePath)) return;

            var stations = new HashSet<string>();
            try
            {
                var dateDirs = Directory.GetDirectories(_basePath);
                foreach (var dDir in dateDirs)
                {
                    var sDirs = Directory.GetDirectories(dDir);
                    foreach (var sDir in sDirs)
                    {
                        stations.Add(Path.GetFileName(sDir));
                    }
                }
            }
            catch { }

            foreach (var s in stations.OrderBy(x => x)) Stations.Add(s);
            if (Stations.Any()) SelectedStation = Stations.First();
        }

        partial void OnSelectedStationChanged(string value) => RefreshDates();

        private void RefreshDates()
        {
            AvailableDates.Clear();
            if (string.IsNullOrEmpty(SelectedStation) || !Directory.Exists(_basePath)) return;

            var dates = new HashSet<DateTime>();
            try
            {
                var dateDirs = Directory.GetDirectories(_basePath);
                foreach (var dDir in dateDirs)
                {
                    if (DateTime.TryParse(Path.GetFileName(dDir), out DateTime dt))
                    {
                        if (Directory.Exists(Path.Combine(dDir, SelectedStation))) dates.Add(dt);
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

            string path = Path.Combine(_basePath, SelectedDate.Value.ToString("yyyy-MM-dd"), SelectedStation);
            if (Directory.Exists(path)) {
                var files = new DirectoryInfo(path).GetFiles("*.mp3").OrderByDescending(f => f.LastWriteTime); 
                foreach (var f in files) RecordingsList.Add(f);
            }
        }

        [RelayCommand] public void ToggleMute() => IsMuted = !IsMuted;

        public void Dispose() { Stop(); if (_stream != 0) Bass.StreamFree(_stream); _playbackTimer.Dispose(); }
    }
}
