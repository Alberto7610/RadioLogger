using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioLogger.Services;
using System.Collections.ObjectModel;
using System.Linq;
using System.Timers;
using RadioLogger.Views; // Fix Views namespace

namespace RadioLogger.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly ConfigManager _configManager = null!;
        private readonly AudioEngine _audioEngine = null!;
        private readonly HeartbeatService _heartbeatService = null!;
        private readonly SignalRService _signalRService = null!;
        private readonly System.Timers.Timer _uiTimer = null!;
        private int _frames = 0;

        [ObservableProperty]
        private string _stationName = "Estación";

        [ObservableProperty]
        private string _statusMessage = "Listo";

        [ObservableProperty]
        private string _storageInfo = "Calculando espacio...";

        [ObservableProperty]
        private string _currentTime = "00:00:00";

        [ObservableProperty]
        private string _currentRecDuration = "00:00:00";

        public ObservableCollection<DeviceViewModel> InputDevices { get; } = new ObservableCollection<DeviceViewModel>();

        public MainViewModel()
        {
            // Evitar ejecución en tiempo de diseño (XAML Designer)
            if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(new System.Windows.DependencyObject()))
            {
                StationName = "Vista Previa (Diseño)";
                return;
            }

            _configManager = new ConfigManager();
            _audioEngine = new AudioEngine(_configManager);
            _heartbeatService = new HeartbeatService(_configManager);
            _heartbeatService.Start();
            
            _signalRService = new SignalRService(_configManager.CurrentSettings);
            _ = _signalRService.StartAsync(); // Fire and forget connection task

            StationName = _configManager.CurrentSettings.StationName;

            LoadDevices();

            _uiTimer = new System.Timers.Timer(50); // 20 FPS
            _uiTimer.Elapsed += (s, e) => 
            {
                UpdateLevels();
                UpdateClock();
                _frames++;
                
                // SignalR batch update (Approx every 200ms = 4 frames)
                if (_frames % 4 == 0)
                {
                    SendSignalRUpdates();
                }

                if (_frames % 200 == 0) // Update disk every ~10 seconds
                {
                    UpdateStorageInfo();
                }
            };
            _uiTimer.Start();
        }

        private void SendSignalRUpdates()
        {
            if (!_signalRService.IsConnected) return;

            var batch = new RadioLogger.Shared.Models.BatchStatusUpdate();
            
            foreach (var d in InputDevices)
            {
                batch.Stations.Add(new RadioLogger.Shared.Models.StationStatusUpdate
                {
                    StationName = d.StationName,
                    LeftLevel = Math.Round(d.LeftLevel, 2),
                    RightLevel = Math.Round(d.RightLevel, 2),
                    IsRecording = d.IsRecording,
                    IsStreaming = d.IsStreaming,
                    StreamUrl = d.StreamUrl,
                    IsSilence = d.IsSilenceDetected,
                    Timestamp = System.DateTime.UtcNow
                });
            }

            _ = _signalRService.SendBatchUpdateAsync(batch);
        }

        private void UpdateLevels()
        {
            foreach (var dev in InputDevices)
            {
                dev.RefreshLevels();
            }
        }

        private void UpdateClock()
        {
            CurrentTime = System.DateTime.Now.ToString("HH:mm:ss");
            
            // Logic for Recording Duration (Sync with ON AIR)
            // Finds the earliest start time among all recording devices
            var activeDevices = InputDevices.Where(d => d.IsRecording && d.RecordingStartTime.HasValue).ToList();
            
            if (activeDevices.Any())
            {
                var minStart = activeDevices.Min(d => d.RecordingStartTime.Value);
                var diff = System.DateTime.Now - minStart;
                
                CurrentRecDuration = $"{diff.Days:D2}d {diff.Hours:D2}h {diff.Minutes:D2}m {diff.Seconds:D2}s";
            }
            else
            {
                CurrentRecDuration = "00d 00h 00m 00s";
            }
        }

        private void UpdateStorageInfo()
        {
            try
            {
                var path = _configManager.CurrentSettings.RecordingBasePath;
                if (!System.IO.Directory.Exists(path))
                    System.IO.Directory.CreateDirectory(path);

                var driveInfo = new System.IO.DriveInfo(path);
                long freeBytes = driveInfo.AvailableFreeSpace;
                double freeGb = freeBytes / 1024.0 / 1024.0 / 1024.0;

                int bitrate = _configManager.CurrentSettings.Mp3Bitrate;
                double bytesPerSecond = (bitrate * 1000) / 8.0;
                double secondsLeft = freeBytes / bytesPerSecond;
                double daysLeft = secondsLeft / 86400.0;

                StorageInfo = $"Disco Libre: {freeGb:F2} GB (Aprox. {daysLeft:F1} días de grabación)";
            }
            catch { StorageInfo = "Error leyendo disco"; }
        }

        [RelayCommand]
        public void OpenPlayer()
        {
            var vm = new PlayerViewModel(_configManager.CurrentSettings.RecordingBasePath);
            var win = new RadioLogger.Views.PlayerWindow { DataContext = vm };
            win.Show(); // Non-modal so you can audit while recording
        }

        [RelayCommand]
        public void OpenSettings()
        {
            var vm = new SettingsViewModel(_configManager, _audioEngine);
            var win = new RadioLogger.Views.SettingsWindow { DataContext = vm };
            if (win.ShowDialog() == true)
            {
                StationName = _configManager.CurrentSettings.StationName;
                LoadDevices();
            }
        }

        [RelayCommand]
        public void ToggleDevice(DeviceViewModel device)
        {
            device.UpdateState();
            
            // Sync AutoRecord list with currently selected devices
            _configManager.CurrentSettings.AutoRecordDevices = InputDevices
                .Where(d => d.IsSelected)
                .Select(d => d.Device.Name)
                .ToList();
                
            SaveConfig();
        }

        [RelayCommand]
        public void ToggleStreaming(DeviceViewModel device)
        {
            device.ToggleStreaming();
        }

        private void SaveConfig()
        {
            _configManager.CurrentSettings.StationName = StationName;
            _configManager.Save();
            StatusMessage = "Configuración guardada.";
        }

        private void LoadDevices()
        {
            var devices = _audioEngine.GetInputDevices();
            var savedActive = _configManager.CurrentSettings.ActiveInputDevices;
            var nameMapping = _configManager.CurrentSettings.DeviceStationNames;
            
            bool showAll = savedActive.Count == 0;

            InputDevices.Clear();

            foreach (var dev in devices)
            {
                if (showAll || savedActive.Contains(dev.Name))
                {
                    if (!nameMapping.TryGetValue(dev.Name, out string? customName) || string.IsNullOrWhiteSpace(customName))
                    {
                        customName = dev.Name;
                    }

                    // Constructor will auto-sync state from Engine
                    var vm = new DeviceViewModel(dev, _audioEngine, _configManager, customName, false);
                    InputDevices.Add(vm);
                }
            }
        }
    }
}