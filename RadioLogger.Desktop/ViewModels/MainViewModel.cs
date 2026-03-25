using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioLogger.Services;
using Serilog;
using System.Collections.ObjectModel;
using System.Linq;
using System.Timers;
using RadioLogger.Views;
using System;

namespace RadioLogger.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private static readonly ILogger _log = AppLog.For<MainViewModel>();

        private readonly ConfigManager _configManager = null!;
        private readonly AudioEngine _audioEngine = null!;
        private readonly HeartbeatService _heartbeatService = null!;
        private readonly SignalRService _signalRService = null!;
        private readonly System.Timers.Timer _uiTimer = null!;
        private System.Timers.Timer? _internetCheckTimer;
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

        [ObservableProperty]
        private string _statusBarColor = "#007ACC"; // Default Blue

        [ObservableProperty]
        private string _internetStatusText = "INTERNET OK";

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

            _log.Information("Aplicación iniciada. PC: {MachineName}", Environment.MachineName);

            _audioEngine = new AudioEngine(_configManager);
            _heartbeatService = new HeartbeatService(_configManager);
            _heartbeatService.Start();

            _signalRService = new SignalRService(_configManager.CurrentSettings);
            _signalRStatus = _configManager.CurrentSettings.IsSignalREnabled ? "Monitoreo: Iniciando..." : "Monitoreo: Desactivado";

            // Timer para checar internet real independientemente del Dashboard
            _internetCheckTimer = new System.Timers.Timer(5000); // Cada 5s
            _internetCheckTimer.Elapsed += (s, e) => CheckInternetReal();
            _internetCheckTimer.Start();

            _signalRService.ConnectionStatusChanged += (msg, connected) =>
            {
                SignalRStatus = msg;
            };

            _signalRService.CommandReceived += OnRemoteCommand;

            _ = _signalRService.StartAsync(); // Fire and forget connection task
            CheckInternetReal(); // Chequeo inicial

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

        private int _internetFailCount = 0;
        private const int MaxInternetFails = 3; // Tolerar 3 fallos (15s) antes de alertar

        private void CheckInternetReal()
        {
            bool isInternetUp = false;
            try
            {
                if (System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
                {
                    using (var ping = new System.Net.NetworkInformation.Ping())
                    {
                        var reply8 = ping.Send("8.8.8.8", 1000);
                        if (reply8.Status == System.Net.NetworkInformation.IPStatus.Success)
                        {
                            isInternetUp = true;
                        }
                        else
                        {
                            var reply1 = ping.Send("1.1.1.1", 1000);
                            if (reply1.Status == System.Net.NetworkInformation.IPStatus.Success)
                            {
                                isInternetUp = true;
                            }
                        }
                    }

                    if (!isInternetUp)
                    {
                        try
                        {
                            using (var client = new System.Net.Http.HttpClient())
                            {
                                client.Timeout = TimeSpan.FromSeconds(5);
                                var response = client.GetAsync("https://cloudradiologger.com/heartbeat").Result;
                                if (response.IsSuccessStatusCode) isInternetUp = true;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { isInternetUp = false; }

            App.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (isInternetUp)
                {
                    _internetFailCount = 0;
                    StatusBarColor = "#007ACC";
                    InternetStatusText = "INTERNET OK";
                }
                else
                {
                    _internetFailCount++;

                    if (_internetFailCount >= MaxInternetFails)
                    {
                        StatusBarColor = "#CC0000";
                        InternetStatusText = "SIN CONEXIÓN A INTERNET";
                        if (_internetFailCount == MaxInternetFails)
                            _log.Warning("Sin acceso a internet confirmado tras {Attempts} intentos", MaxInternetFails);
                    }
                    else
                    {
                        InternetStatusText = $"INTERNET INESTABLE ({_internetFailCount})";
                    }
                }
            });
        }

        private string GetHardwareId()
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard");
                foreach (var item in searcher.Get())
                {
                    return item["SerialNumber"]?.ToString() ?? "UNKNOWN_HWID";
                }
            }
            catch { }
            return "DEV_MACHINE_" + Environment.MachineName;
        }

        private void SendSignalRUpdates()
        {
            if (!_signalRService.IsConnected) return;

            var batch = new RadioLogger.Shared.Models.BatchStatusUpdate { MachineId = Environment.MachineName };
            var hwid = GetHardwareId();

            foreach (var d in InputDevices)
            {
                batch.Stations.Add(new RadioLogger.Shared.Models.StationStatusUpdate
                {
                    MachineId = Environment.MachineName,
                    StationName = d.StationName,
                    HardwareName = d.Device.Name,
                    HardwareId = hwid,
                    LicenseKey = "FREE-TRIAL-001",
                    LeftLevel = Math.Round(d.LeftLevel / 100.0, 3),
                    RightLevel = Math.Round(d.RightLevel / 100.0, 3),
                    LeftPeak = Math.Round(d.LeftPeak / 100.0, 3),
                    RightPeak = Math.Round(d.RightPeak / 100.0, 3),
                    IsRecording = d.IsRecording,
                    RecordingStartTime = d.RecordingStartTime,
                    IsStreaming = d.IsStreaming,
                    StreamUrl = d.StreamUrl,
                    IsSilence = d.IsSilenceDetected,
                    IsRecordingEnabled = d.IsRecordingEnabled,
                    Timestamp = DateTime.UtcNow
                });
            }

            _ = _signalRService.SendBatchUpdateAsync(batch);
        }

        private void UpdateLevels()
        {
            foreach (var dev in InputDevices)
            {
                bool previouslySilence = dev.IsSilenceDetected;
                dev.RefreshLevels();

                if (_configManager.CurrentSettings.EnableTelegram)
                {
                    if (dev.IsSilenceDetected && !previouslySilence)
                    {
                        _ = TelegramService.SendAlertAsync(
                            _configManager.CurrentSettings.TelegramToken,
                            _configManager.CurrentSettings.TelegramChatId,
                            $"🔴 <b>ALERTA DE SILENCIO</b>\nEstación: <b>{dev.StationName}</b>\nHora: {DateTime.Now:HH:mm:ss}\nEstado: No se detecta audio.");
                    }
                    else if (!dev.IsSilenceDetected && previouslySilence)
                    {
                        _ = TelegramService.SendAlertAsync(
                            _configManager.CurrentSettings.TelegramToken,
                            _configManager.CurrentSettings.TelegramChatId,
                            $"🟢 <b>AUDIO RESTABLECIDO</b>\nEstación: <b>{dev.StationName}</b>\nHora: {DateTime.Now:HH:mm:ss}\nDuración del silencio: {dev.SilenceDuration}");
                    }
                }
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
                _log.Warning(ex, "Error al abrir dashboard");
            }
        }

        [RelayCommand]
        public void OpenSettings()
        {
            var dialog = new RadioLogger.Views.PasswordDialog(_configManager.CurrentSettings.SettingsPasswordHash);
            dialog.Owner = System.Windows.Application.Current.MainWindow;
            if (dialog.ShowDialog() != true || !dialog.IsAuthenticated) return;

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

            if (device.IsSelected)
                _log.Information("Grabación INICIADA manualmente: {Station}", device.StationName);
            else
                _log.Information("Grabación DETENIDA manualmente: {Station}", device.StationName);

            PersistCurrentState();
        }

        [RelayCommand]
        public void ToggleStreaming(DeviceViewModel device)
        {
            device.ToggleStreaming();
            PersistCurrentState();
        }

        private void OnRemoteCommand(RadioLogger.Shared.Models.StationCommand command)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                var device = InputDevices.FirstOrDefault(d => d.Device.Name == command.HardwareName);
                if (device == null) return;

                _log.Information("Comando remoto: {Command} para {HardwareName}", command.Command, command.HardwareName);

                switch (command.Command)
                {
                    case "START_RECORDING":
                        if (!device.IsRecording)
                        {
                            device.IsSelected = true;
                            device.UpdateState();
                        }
                        break;

                    case "STOP_RECORDING":
                        if (device.IsRecording)
                        {
                            device.IsSelected = false;
                            device.UpdateState();
                        }
                        break;

                    case "START_STREAMING":
                        if (device.IsRecording && !device.IsStreaming)
                        {
                            device.ToggleStreaming();
                        }
                        break;

                    case "STOP_STREAMING":
                        if (device.IsStreaming)
                        {
                            device.ToggleStreaming();
                        }
                        break;

                    case "RESET_PASSWORD":
                        if (!string.IsNullOrEmpty(command.Payload))
                        {
                            _configManager.CurrentSettings.SettingsPasswordHash = command.Payload;
                            _configManager.Save();
                            _log.Information("Password de configuración reseteado remotamente");
                        }
                        break;
                }

                PersistCurrentState();
            });
        }

        public void PersistCurrentState()
        {
            _configManager.CurrentSettings.AutoRecordDevices = InputDevices
                .Where(d => d.IsSelected)
                .Select(d => d.Device.Name)
                .ToList();
            _configManager.CurrentSettings.AutoStreamDevices = InputDevices
                .Where(d => d.IsStreaming)
                .Select(d => d.Device.Name)
                .ToList();
            SaveConfig();
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
            var autoRecord = _configManager.CurrentSettings.AutoRecordDevices;
            var autoStream = _configManager.CurrentSettings.AutoStreamDevices;

            bool showAll = savedActive.Count == 0;

            var targetDevices = devices
                .Where(dev => showAll || savedActive.Contains(dev.Name))
                .ToList();

            var toRemove = InputDevices
                .Where(vm => !targetDevices.Any(td => td.Name == vm.Device.Name))
                .ToList();
            foreach (var vm in toRemove) InputDevices.Remove(vm);

            foreach (var td in targetDevices)
            {
                if (!nameMapping.TryGetValue(td.Name, out string? customName) || string.IsNullOrWhiteSpace(customName))
                {
                    customName = td.Name;
                }

                var recEnabled = true;
                if (_configManager.CurrentSettings.DeviceRecordingEnabled.TryGetValue(td.Name, out bool re))
                    recEnabled = re;

                var existing = InputDevices.FirstOrDefault(vm => vm.Device.Name == td.Name);
                if (existing != null)
                {
                    if (existing.StationName != customName)
                        existing.StationName = customName;
                    existing.IsRecordingEnabled = recEnabled;
                }
                else
                {
                    var vm = new DeviceViewModel(td, _audioEngine, _configManager, customName, false);
                    InputDevices.Add(vm);
                }
            }

            RestorePreviousState(autoRecord, autoStream);
        }

        public void Cleanup()
        {
            _uiTimer?.Stop();
            _uiTimer?.Dispose();
            _internetCheckTimer?.Stop();
            _internetCheckTimer?.Dispose();
            _heartbeatService?.Stop();
            _heartbeatService?.Dispose();
            _signalRService?.Dispose();
        }

        private void RestorePreviousState(List<string> autoRecord, List<string> autoStream)
        {
            if (autoRecord.Count == 0 && autoStream.Count == 0) return;

            _log.Information("Restaurando estado previo: {RecCount} grabando, {StreamCount} en streaming", autoRecord.Count, autoStream.Count);

            foreach (var device in InputDevices)
            {
                if (autoRecord.Contains(device.Device.Name) && !device.IsRecording)
                {
                    device.IsSelected = true;
                    device.UpdateState();
                    _log.Information("Auto-restaurado grabación: {Station}", device.StationName);
                }
            }

            if (autoStream.Count > 0)
            {
                var streamDeviceNames = autoStream.ToList();
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    _ = System.Threading.Tasks.Task.Run(async () =>
                    {
                        await System.Threading.Tasks.Task.Delay(2000);
                        dispatcher.Invoke(() =>
                        {
                            foreach (var device in InputDevices)
                            {
                                if (streamDeviceNames.Contains(device.Device.Name) && device.IsRecording && !device.IsStreaming)
                                {
                                    device.ToggleStreaming();
                                    _log.Information("Auto-restaurado streaming: {Station}", device.StationName);
                                }
                                else if (streamDeviceNames.Contains(device.Device.Name) && !device.IsRecording)
                                {
                                    _log.Warning("No se pudo restaurar streaming para {Station}: grabación no activa", device.StationName);
                                }
                            }
                        });
                    });
                }
            }
        }
    }
}
