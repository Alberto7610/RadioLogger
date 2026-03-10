using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioLogger.Services;
using System.Collections.ObjectModel;
using System.Linq;
using System.Timers;
using RadioLogger.Views;
using System;

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

        [ObservableProperty]
        private string _signalRStatus = "Monitoreo: Desactivado";

        public string DashboardUrl => _configManager.CurrentSettings.SignalRHubUrl.Replace("/radiohub", "");

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
            _signalRStatus = _configManager.CurrentSettings.IsSignalREnabled ? "Monitoreo: Iniciando..." : "Monitoreo: Desactivado";
            _signalRService.ConnectionStatusChanged += (msg, connected) => SignalRStatus = msg;
            
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

            var batch = new RadioLogger.Shared.Models.BatchStatusUpdate { MachineId = Environment.MachineName };
            
            foreach (var d in InputDevices)
            {
                batch.Stations.Add(new RadioLogger.Shared.Models.StationStatusUpdate
                {
                    MachineId = Environment.MachineName,
                    StationName = d.StationName,
                    LeftLevel = Math.Round(d.LeftLevel / 100.0, 3),
                    RightLevel = Math.Round(d.RightLevel / 100.0, 3),
                    LeftPeak = Math.Round(d.LeftPeak / 100.0, 3),
                    RightPeak = Math.Round(d.RightPeak / 100.0, 3),
                    IsRecording = d.IsRecording,
                    IsStreaming = d.IsStreaming,
                    StreamUrl = d.StreamUrl,
                    IsSilence = d.IsSilenceDetected,
                    Timestamp = DateTime.UtcNow
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
            CurrentTime = DateTime.Now.ToString("HH:mm:ss");
            
            var activeDevices = InputDevices.Where(d => d.IsRecording && d.RecordingStartTime.HasValue).ToList();
            
            if (activeDevices.Any())
            {
                var minStart = activeDevices.Min(d => d.RecordingStartTime!.Value);
                var diff = DateTime.Now - minStart;
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
            win.Show();
        }

        [RelayCommand]
        public void OpenDashboard()
        {
            try
            {
                var url = DashboardUrl;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                StatusMessage = "No se pudo abrir el dashboard web.";
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
        }

        [RelayCommand]
        public void OpenSettings()
        {
            var vm = new SettingsViewModel(_configManager, _audioEngine);
            var win = new RadioLogger.Views.SettingsWindow { DataContext = vm };
            if (win.ShowDialog() == true)
            {
                StationName = _configManager.CurrentSettings.StationName;
                _audioEngine.UpdateAllSettings(_configManager.CurrentSettings);
                LoadDevices();
            }
        }

        [RelayCommand]
        public void ToggleDevice(DeviceViewModel device)
        {
            device.UpdateState();
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

            // Determine target devices to show
            var targetDevices = devices
                .Where(dev => showAll || savedActive.Contains(dev.Name))
                .ToList();

            // 1. Remove devices that are no longer active
            var toRemove = InputDevices
                .Where(vm => !targetDevices.Any(td => td.Name == vm.Device.Name))
                .ToList();
            foreach (var vm in toRemove) InputDevices.Remove(vm);

            // 2. Update or Add devices
            foreach (var td in targetDevices)
            {
                if (!nameMapping.TryGetValue(td.Name, out string? customName) || string.IsNullOrWhiteSpace(customName))
                {
                    customName = td.Name;
                }

                var existing = InputDevices.FirstOrDefault(vm => vm.Device.Name == td.Name);
                if (existing != null)
                {
                    // Update only the StationName if changed
                    if (existing.StationName != customName)
                    {
                        existing.StationName = customName;
                    }
                }
                else
                {
                    // Add as new device
                    var vm = new DeviceViewModel(td, _audioEngine, _configManager, customName, false);
                    InputDevices.Add(vm);
                }
            }
        }
    }
}
