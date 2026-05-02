using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioLogger.Models;
using RadioLogger.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Forms; // For FolderBrowserDialog

namespace RadioLogger.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly ConfigManager _configManager;
        private readonly AudioEngine _audioEngine;
        private readonly LicenseService _licenseService;

        [ObservableProperty]
        private string _stationName = string.Empty;

        [ObservableProperty]
        private string _recordingPath = string.Empty;

        [ObservableProperty]
        private int _bitrate;

        [ObservableProperty]
        private int _segmentDuration;

        [ObservableProperty]
        private int _startHour;

        [ObservableProperty]
        private int _endHour;

        // Wrapper for selection in UI
        public class DeviceSelection : ObservableObject
        {
            public string Name { get; set; } = string.Empty;
            public string Driver { get; set; } = string.Empty;
            
            private bool _isActive;
            public bool IsActive 
            { 
                get => _isActive; 
                set => SetProperty(ref _isActive, value); 
            }

            private string _customStationName = string.Empty;
            public string CustomStationName
            {
                get => _customStationName;
                set => SetProperty(ref _customStationName, value);
            }

            private bool _isRecordingEnabled = true;
            public bool IsRecordingEnabled
            {
                get => _isRecordingEnabled;
                set => SetProperty(ref _isRecordingEnabled, value);
            }
        }

        public ObservableCollection<DeviceSelection> AvailableDevices { get; } = new ObservableCollection<DeviceSelection>();

        [ObservableProperty]
        private DeviceSelection? _selectedStreamingDevice;

        [ObservableProperty] private string _streamHost = "127.0.0.1";
        [ObservableProperty] private int _streamPort = 8000;
        [ObservableProperty] private string _streamPassword = "";
        [ObservableProperty] private string _streamMount = "/live";
        [ObservableProperty] private int _streamBitrate = 128;
        [ObservableProperty] private string _streamServerType = "Shoutcast";
        [ObservableProperty] private int _streamSampleRate = 44100;
        [ObservableProperty] private int _streamChannels = 2;
        [ObservableProperty] private string _streamUsername = "source";
        [ObservableProperty] private string _streamGenre = "";

        [ObservableProperty] private string _testStatus = "";
        [ObservableProperty] private string _testStatusColor = "#666"; // Default Grey

        // SignalR
        [ObservableProperty] private bool _isSignalREnabled;
        [ObservableProperty] private string _signalRHubUrl = string.Empty;

        // Pairing
        [ObservableProperty] private string _pairingCode = "";
        [ObservableProperty] private string _pairingStatusText = "";
        [ObservableProperty] private string _pairingStatusColor = "#666";
        [ObservableProperty] private string _pairingResultText = "";
        [ObservableProperty] private string _pairingResultColor = "#666";
        private SignalRService? _signalRService;

        [ObservableProperty] private bool _isAutoStartEnabled;

        // Auto-Login Windows
        [ObservableProperty] private bool _isAutoLoginEnabled;
        [ObservableProperty] private string _autoLoginUsername = Environment.UserName;
        [ObservableProperty] private string _autoLoginPassword = "";
        [ObservableProperty] private string _autoLoginStatus = "";
        [ObservableProperty] private string _autoLoginStatusColor = "#666";

        [ObservableProperty] private string _currentPassword = "";
        [ObservableProperty] private string _newPassword = "";
        [ObservableProperty] private string _confirmPassword = "";
        [ObservableProperty] private string _passwordStatus = "";
        [ObservableProperty] private string _passwordStatusColor = "#666";

        // === LICENSE ===
        [ObservableProperty] private string _licenseStatusText = "";
        [ObservableProperty] private string _licenseStatusColor = "#666";
        [ObservableProperty] private string _licenseKey = "—";
        [ObservableProperty] private string _licenseType = "—";
        [ObservableProperty] private string _licenseExpiry = "—";
        [ObservableProperty] private string _offlineCode = "";
        [ObservableProperty] private string _offlineCodeStatus = "";
        [ObservableProperty] private string _offlineCodeStatusColor = "#666";

        // === LOG VIEWER ===
        public ObservableCollection<string> LogDates { get; } = new();
        public ObservableCollection<string> LogStations { get; } = new();
        public ObservableCollection<string> FilteredLogLines { get; } = new();

        [ObservableProperty] private string? _selectedLogDate;
        [ObservableProperty] private string? _selectedLogDateEnd;
        [ObservableProperty] private string _selectedLogStation = "Todas";
        [ObservableProperty] private string _selectedLogLevel = "Todos";
        [ObservableProperty] private string _logSearchText = "";
        [ObservableProperty] private string _logDateDisplay = "";
        [ObservableProperty] private string _logLineCount = "";
        [ObservableProperty] private string _logTimeFrom = "";
        [ObservableProperty] private string _logTimeTo = "";

        private List<string> _allLogLines = new();
        private string _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        private System.Timers.Timer? _logRefreshTimer;
        private long _lastLogFileSize;

        partial void OnSelectedLogDateChanged(string? value) => LoadLogFiles();
        partial void OnSelectedLogDateEndChanged(string? value) => LoadLogFiles();

        public void StartLogAutoRefresh()
        {
            _logRefreshTimer?.Stop();
            _logRefreshTimer = new System.Timers.Timer(3000); // cada 3 segundos
            _logRefreshTimer.Elapsed += (s, e) => AutoRefreshLogs();
            _logRefreshTimer.Start();
        }

        public void StopLogAutoRefresh()
        {
            _logRefreshTimer?.Stop();
            _logRefreshTimer?.Dispose();
            _logRefreshTimer = null;
        }

        private void AutoRefreshLogs()
        {
            if (string.IsNullOrEmpty(SelectedLogDate)) return;

            // Solo refrescar si estamos viendo el log de hoy
            var todayFile = "radiologger-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log";
            var endFile = SelectedLogDateEnd ?? SelectedLogDate;
            if (SelectedLogDate != todayFile && endFile != todayFile) return;

            var filePath = Path.Combine(_logDirectory, todayFile);
            if (!File.Exists(filePath)) return;

            try
            {
                var fi = new FileInfo(filePath);
                if (fi.Length == _lastLogFileSize) return; // sin cambios
                _lastLogFileSize = fi.Length;
            }
            catch { return; }

            // Recargar en UI thread
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => LoadLogFiles());
        }
        partial void OnSelectedLogStationChanged(string value) => ApplyLogFilter();
        partial void OnSelectedLogLevelChanged(string value) => ApplyLogFilter();
        partial void OnLogSearchTextChanged(string value) => ApplyLogFilter();
        partial void OnLogTimeFromChanged(string value) => ApplyLogFilter();
        partial void OnLogTimeToChanged(string value) => ApplyLogFilter();

        [RelayCommand]
        public void ApplyOfflineCode()
        {
            if (string.IsNullOrWhiteSpace(OfflineCode))
            {
                OfflineCodeStatus = "Introduce un código";
                OfflineCodeStatusColor = "#FF4444";
                return;
            }

            var licService = _licenseService;
            var hwId = MachineInfoCollector.GetHardwareIdStatic();
            var (success, message) = licService.ApplyOfflineCode(OfflineCode, hwId);

            OfflineCodeStatus = message;
            OfflineCodeStatusColor = success ? "#00CC66" : "#FF4444";

            if (success)
            {
                LoadLicenseInfo();
                OfflineCode = "";
            }
        }

        private void LoadLicenseInfo()
        {
            var lic = _configManager.CurrentSettings.License;
            if (lic != null && lic.IsValid)
            {
                var licService = _licenseService;
                LicenseStatusText = licService.StatusMessage;
                LicenseStatusColor = licService.CurrentStatus switch
                {
                    LicenseStatus.Valid => "#00CC66",
                    LicenseStatus.GracePeriod => "#FFAA00",
                    _ => "#FF4444"
                };
                LicenseKey = lic.Key;
                LicenseType = $"{lic.LicenseType} ({lic.MaxSlots} slots)";
                LicenseExpiry = lic.ExpirationDate.ToLocalTime().ToString("dd/MM/yyyy");
            }
            else
            {
                LicenseStatusText = "Sin licencia — esperando activación del Dashboard";
                LicenseStatusColor = "#666";
                LicenseKey = "—";
                LicenseType = "—";
                LicenseExpiry = "—";
            }
        }

        [RelayCommand]
        public void RefreshLogs()
        {
            var currentDate = SelectedLogDate;
            var currentDateEnd = SelectedLogDateEnd;
            LoadLogDates();

            // Restaurar selección sin depender del evento Changed
            if (currentDate != null && LogDates.Contains(currentDate))
                _selectedLogDate = currentDate;
            if (currentDateEnd != null && LogDates.Contains(currentDateEnd))
                _selectedLogDateEnd = currentDateEnd;

            OnPropertyChanged(nameof(SelectedLogDate));
            OnPropertyChanged(nameof(SelectedLogDateEnd));

            // Forzar recarga siempre
            LoadLogFiles();
        }

        private void LoadLogDates()
        {
            LogDates.Clear();
            if (!Directory.Exists(_logDirectory)) return;

            var files = Directory.GetFiles(_logDirectory, "radiologger-*.log")
                .OrderByDescending(f => f)
                .ToList();

            foreach (var file in files)
            {
                LogDates.Add(Path.GetFileName(file));
            }

            if (LogDates.Any())
                SelectedLogDate = LogDates.First();
        }

        private void LoadLogFiles()
        {
            _allLogLines.Clear();
            FilteredLogLines.Clear();
            LogStations.Clear();
            LogStations.Add("Todas");

            if (string.IsNullOrEmpty(SelectedLogDate))
            {
                LogDateDisplay = "";
                LogLineCount = "";
                return;
            }

            // Determinar rango de archivos a cargar
            var startFile = SelectedLogDate;
            var endFile = SelectedLogDateEnd ?? SelectedLogDate;

            // Asegurar orden correcto
            if (string.Compare(startFile, endFile, StringComparison.Ordinal) > 0)
                (startFile, endFile) = (endFile, startFile);

            // Mostrar rango en display
            var startDateStr = startFile.Replace("radiologger-", "").Replace(".log", "");
            var endDateStr = endFile.Replace("radiologger-", "").Replace(".log", "");
            var culture = new System.Globalization.CultureInfo("es-MX");

            if (startFile == endFile)
            {
                if (DateTime.TryParse(startDateStr, out var dt))
                    LogDateDisplay = dt.ToString("dd/MM/yyyy — dddd", culture);
                else
                    LogDateDisplay = startDateStr;
            }
            else
            {
                var d1 = DateTime.TryParse(startDateStr, out var dt1) ? dt1.ToString("dd/MM/yyyy", culture) : startDateStr;
                var d2 = DateTime.TryParse(endDateStr, out var dt2) ? dt2.ToString("dd/MM/yyyy", culture) : endDateStr;
                LogDateDisplay = d1 + " → " + d2;
            }

            // Cargar todos los archivos en el rango
            var stationSet = new HashSet<string>();
            var filesToLoad = LogDates
                .Where(f => string.Compare(f, startFile, StringComparison.Ordinal) >= 0 &&
                            string.Compare(f, endFile, StringComparison.Ordinal) <= 0)
                .OrderBy(f => f)
                .ToList();

            foreach (var fileName in filesToLoad)
            {
                var filePath = Path.Combine(_logDirectory, fileName);
                if (!File.Exists(filePath)) continue;

                try
                {
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(fs);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        _allLogLines.Add(line);

                        var sourceContext = ExtractBracketValue(line, 2);
                        if (!string.IsNullOrEmpty(sourceContext))
                            stationSet.Add(sourceContext);
                    }
                }
                catch { }
            }

            foreach (var s in stationSet.OrderBy(s => s))
                LogStations.Add(s);

            SelectedLogStation = "Todas";
            ApplyLogFilter();
        }

        /// <summary>
        /// Extracts the Nth bracketed value from a log line.
        /// Line format: "2026-03-25 14:32:15.443 [INF] [AudioChannel] message..."
        /// Index 0 = level tag, index 1 = source context, etc.
        /// </summary>
        private static string? ExtractBracketValue(string line, int bracketIndex)
        {
            int current = 0;
            int pos = 0;
            while (current <= bracketIndex && pos < line.Length)
            {
                int open = line.IndexOf('[', pos);
                if (open < 0) return null;
                int close = line.IndexOf(']', open);
                if (close < 0) return null;

                if (current == bracketIndex)
                    return line[(open + 1)..close];

                current++;
                pos = close + 1;
            }
            return null;
        }

        private void ApplyLogFilter()
        {
            FilteredLogLines.Clear();

            var levelFilter = SelectedLogLevel;
            var stationFilter = SelectedLogStation;
            var searchFilter = LogSearchText?.Trim() ?? "";
            var timeFrom = LogTimeFrom?.Trim() ?? "";
            var timeTo = LogTimeTo?.Trim() ?? "";

            // Parsear filtros de hora (formato HH:mm o HH:mm:ss)
            TimeSpan? tsFrom = TimeSpan.TryParse(timeFrom, out var tf) ? tf : null;
            TimeSpan? tsTo = TimeSpan.TryParse(timeTo, out var tt) ? tt : null;

            foreach (var rawLine in _allLogLines)
            {
                // Filter by level
                if (levelFilter != "Todos" && !rawLine.Contains($"[{levelFilter}]"))
                    continue;

                // Filter by station (source context)
                if (stationFilter != "Todas")
                {
                    var source = ExtractBracketValue(rawLine, 2);
                    if (source != stationFilter)
                        continue;
                }

                // Filter by time range (extraer HH:mm:ss del log)
                if (tsFrom.HasValue || tsTo.HasValue)
                {
                    if (rawLine.Length > 24 && rawLine[10] == ' ' && rawLine[13] == ':')
                    {
                        var timeStr = rawLine.Substring(11, 8); // "HH:mm:ss"
                        if (TimeSpan.TryParse(timeStr, out var lineTime))
                        {
                            if (tsFrom.HasValue && lineTime < tsFrom.Value) continue;
                            if (tsTo.HasValue && lineTime > tsTo.Value) continue;
                        }
                    }
                }

                // Filter by search text
                if (!string.IsNullOrEmpty(searchFilter) &&
                    !rawLine.Contains(searchFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Strip date prefix, keep only time: "2026-03-25 14:32:15.443 ..." → "14:32:15.443 ..."
                var displayLine = rawLine;
                if (rawLine.Length > 24 && rawLine[4] == '-' && rawLine[10] == ' ')
                    displayLine = rawLine[11..]; // Skip "2026-03-25 "

                FilteredLogLines.Add(displayLine);
            }

            LogLineCount = $"{FilteredLogLines.Count:N0} / {_allLogLines.Count:N0} entradas";
        }

        [RelayCommand]
        public void CopySelectedLogs(System.Collections.IList? selectedItems)
        {
            if (selectedItems == null || selectedItems.Count == 0) return;

            var sb = new StringBuilder();
            foreach (string line in selectedItems)
                sb.AppendLine(line);

            for (int i = 0; i < 5; i++)
            {
                try
                {
                    System.Windows.Clipboard.SetText(sb.ToString());
                    return;
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    System.Threading.Thread.Sleep(50);
                }
            }
        }

        [RelayCommand]
        public void ExportLogs()
        {
            if (FilteredLogLines.Count == 0) return;

            var dialog = new SaveFileDialog
            {
                Filter = "Archivo de texto (*.txt)|*.txt|CSV (*.csv)|*.csv|Todos (*.*)|*.*",
                FileName = "radiologger-export-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"),
                DefaultExt = ".txt"
            };

            if (dialog.ShowDialog() != DialogResult.OK) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("// RadioLogger Export — " + LogDateDisplay + " — " + LogLineCount);
                sb.AppendLine("// Filtros: Nivel=" + SelectedLogLevel + ", Fuente=" + SelectedLogStation +
                    (string.IsNullOrEmpty(LogSearchText) ? "" : ", Buscar=" + LogSearchText) +
                    (string.IsNullOrEmpty(LogTimeFrom) ? "" : ", Desde=" + LogTimeFrom) +
                    (string.IsNullOrEmpty(LogTimeTo) ? "" : ", Hasta=" + LogTimeTo));
                sb.AppendLine();

                foreach (var line in FilteredLogLines)
                    sb.AppendLine(line);

                File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error al exportar: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void ChangePassword()
        {
            if (string.IsNullOrEmpty(NewPassword))
            {
                PasswordStatus = "La nueva contraseña no puede estar vacía";
                PasswordStatusColor = "#FF4444";
                return;
            }
            if (NewPassword != ConfirmPassword)
            {
                PasswordStatus = "Las contraseñas no coinciden";
                PasswordStatusColor = "#FF4444";
                return;
            }
            if (NewPassword.Length < 4)
            {
                PasswordStatus = "Mínimo 4 caracteres";
                PasswordStatusColor = "#FF4444";
                return;
            }

            // Verify current password (supports both BCrypt and legacy SHA256)
            var storedHash = _configManager.CurrentSettings.SettingsPasswordHash;
            bool currentValid;
            if (storedHash.Length == 64 && !storedHash.StartsWith("$2"))
            {
                // Legacy SHA256
                var sha256Bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(CurrentPassword));
                currentValid = Convert.ToHexStringLower(sha256Bytes) == storedHash;
            }
            else
            {
                currentValid = RadioLogger.Views.PasswordDialog.VerifyPassword(CurrentPassword, storedHash);
            }

            if (!currentValid)
            {
                PasswordStatus = "Contraseña actual incorrecta";
                PasswordStatusColor = "#FF4444";
                return;
            }

            // Always hash new password with BCrypt
            _configManager.CurrentSettings.SettingsPasswordHash = RadioLogger.Views.PasswordDialog.HashPassword(NewPassword);
            PasswordStatus = "Contraseña actualizada correctamente";
            PasswordStatusColor = "#00CC66";
            CurrentPassword = "";
            NewPassword = "";
            ConfirmPassword = "";
        }

        [RelayCommand]
        public void ApplyAutoLogin()
        {
            if (IsAutoLoginEnabled)
            {
                if (string.IsNullOrWhiteSpace(AutoLoginUsername) || string.IsNullOrEmpty(AutoLoginPassword))
                {
                    AutoLoginStatus = "Ingrese usuario y contraseña de Windows";
                    AutoLoginStatusColor = "#FF4444";
                    return;
                }

                var (success, message) = AutoStartService.SetAutoLogin(true, AutoLoginUsername, AutoLoginPassword);
                AutoLoginStatus = message;
                AutoLoginStatusColor = success ? "#00CC66" : "#FF4444";

                if (success)
                {
                    _configManager.CurrentSettings.IsAutoLoginEnabled = true;
                    _configManager.CurrentSettings.AutoLoginUsername = AutoLoginUsername;
                }
            }
            else
            {
                var (success, message) = AutoStartService.SetAutoLogin(false);
                AutoLoginStatus = message;
                AutoLoginStatusColor = success ? "#00CC66" : "#FF4444";

                if (success)
                    _configManager.CurrentSettings.IsAutoLoginEnabled = false;
            }

            _configManager.Save();
        }

        [RelayCommand]
        public async System.Threading.Tasks.Task TestConnection()
        {
            TestStatus = "Conectando...";
            TestStatusColor = "#AAA"; // Waiting

            var tempConfig = new StreamingConfig
            {
                Host = StreamHost,
                Port = StreamPort,
                Password = StreamPassword,
                MountPoint = StreamMount,
                ServerType = StreamServerType
            };

            var (success, msg) = await _audioEngine.TestConnectionAsync(tempConfig);

            TestStatus = msg;
            TestStatusColor = success ? "#00FF00" : "#FF0000"; // Green vs Red
        }

        partial void OnSelectedStreamingDeviceChanged(DeviceSelection? value)
        {
            TestStatus = ""; // Clear status on switch
            if (value != null)
            {
                var configs = _configManager.CurrentSettings.DeviceStreamingConfigs;
                if (!configs.ContainsKey(value.Name))
                {
                    configs[value.Name] = new StreamingConfig();
                }
                var conf = configs[value.Name];
                
                // Load into UI
                StreamHost = conf.Host;
                StreamPort = conf.Port;
                StreamPassword = conf.Password;
                StreamMount = conf.MountPoint;
                StreamBitrate = conf.Bitrate;
                StreamServerType = conf.ServerType;
                StreamSampleRate = conf.SampleRate;
                StreamChannels = conf.Channels;
                StreamUsername = conf.Username;
                StreamGenre = conf.Genre;
            }
        }

        // Methods to sync back to config object when UI changes
        partial void OnStreamHostChanged(string value) => UpdateCurrentStreamConfig(c => c.Host = value);
        partial void OnStreamPortChanged(int value) => UpdateCurrentStreamConfig(c => c.Port = value);
        partial void OnStreamPasswordChanged(string value) => UpdateCurrentStreamConfig(c => c.Password = value);
        partial void OnStreamMountChanged(string value) => UpdateCurrentStreamConfig(c => c.MountPoint = value);
        partial void OnStreamBitrateChanged(int value) => UpdateCurrentStreamConfig(c => c.Bitrate = value);
        partial void OnStreamServerTypeChanged(string value) => UpdateCurrentStreamConfig(c => c.ServerType = value);
        partial void OnStreamSampleRateChanged(int value) => UpdateCurrentStreamConfig(c => c.SampleRate = value);
        partial void OnStreamChannelsChanged(int value) => UpdateCurrentStreamConfig(c => c.Channels = value);
        partial void OnStreamUsernameChanged(string value) => UpdateCurrentStreamConfig(c => c.Username = value);
        partial void OnStreamGenreChanged(string value) => UpdateCurrentStreamConfig(c => c.Genre = value);

        private void UpdateCurrentStreamConfig(Action<StreamingConfig> action)
        {
            if (SelectedStreamingDevice != null)
            {
                var configs = _configManager.CurrentSettings.DeviceStreamingConfigs;
                if (configs.TryGetValue(SelectedStreamingDevice.Name, out var conf))
                {
                    action(conf);
                }
            }
        }

        [RelayCommand]
        public async System.Threading.Tasks.Task Pair()
        {
            if (string.IsNullOrWhiteSpace(PairingCode) || PairingCode.Length != 6)
            {
                PairingResultText = "Ingresa un código de 6 dígitos";
                PairingResultColor = "#FF4444";
                return;
            }

            if (_signalRService == null || !_signalRService.IsConnected)
            {
                PairingResultText = "No hay conexión con el servidor. Verifica la URL.";
                PairingResultColor = "#FF4444";
                return;
            }

            PairingResultText = "Validando código...";
            PairingResultColor = "#AAA";

            var result = await _signalRService.PairAsync(PairingCode);

            if (result.Success)
            {
                PairingResultText = "Emparejamiento exitoso";
                PairingResultColor = "#00CC66";
                PairingStatusText = "EMPAREJADO";
                PairingStatusColor = "#00CC66";
                PairingCode = "";
                _configManager.Save();
            }
            else
            {
                PairingResultText = result.Error ?? "Error desconocido";
                PairingResultColor = "#FF4444";
            }
        }

        public SettingsViewModel(ConfigManager configManager, AudioEngine audioEngine, LicenseService? licenseService = null, SignalRService? signalRService = null)
        {
            _configManager = configManager;
            _audioEngine = audioEngine;
            _licenseService = licenseService ?? new LicenseService(configManager);
            _signalRService = signalRService;

            // Load current values
            StationName = _configManager.CurrentSettings.StationName;
            RecordingPath = _configManager.CurrentSettings.RecordingBasePath;
            Bitrate = _configManager.CurrentSettings.Mp3Bitrate;
            SegmentDuration = _configManager.CurrentSettings.SegmentDurationMinutes;
            StartHour = _configManager.CurrentSettings.StartHour;
            EndHour = _configManager.CurrentSettings.EndHour;

            IsSignalREnabled = _configManager.CurrentSettings.IsSignalREnabled;
            SignalRHubUrl = _configManager.CurrentSettings.SignalRHubUrl;
            IsAutoStartEnabled = AutoStartService.IsAutoStartEnabled();

            // Pairing status
            if (!string.IsNullOrEmpty(_configManager.CurrentSettings.SignalRApiKey))
            {
                PairingStatusText = "EMPAREJADO";
                PairingStatusColor = "#00CC66";
            }
            else
            {
                PairingStatusText = "NO EMPAREJADO — Ingresa un código del Dashboard";
                PairingStatusColor = "#FFAA00";
            }
            IsAutoLoginEnabled = AutoStartService.IsAutoLoginEnabled();
            if (!string.IsNullOrEmpty(_configManager.CurrentSettings.AutoLoginUsername))
                AutoLoginUsername = _configManager.CurrentSettings.AutoLoginUsername;

            LoadDevices();
            LoadLogDates();
            LoadLicenseInfo();
            StartLogAutoRefresh();
        }

        private void LoadDevices()
        {
            var systemDevices = _audioEngine.GetInputDevices();
            var savedActive = _configManager.CurrentSettings.ActiveInputDevices;
            var nameMapping = _configManager.CurrentSettings.DeviceStationNames;
            var recMapping = _configManager.CurrentSettings.DeviceRecordingEnabled;

            foreach (var dev in systemDevices)
            {
                if (!nameMapping.TryGetValue(dev.Name, out string? customName) || string.IsNullOrWhiteSpace(customName))
                {
                    customName = dev.Name;
                }

                var recEnabled = true;
                if (recMapping.TryGetValue(dev.Name, out bool re))
                    recEnabled = re;

                AvailableDevices.Add(new DeviceSelection
                {
                    Name = dev.Name,
                    Driver = dev.Driver,
                    IsActive = savedActive.Contains(dev.Name),
                    CustomStationName = customName,
                    IsRecordingEnabled = recEnabled
                });
            }
            
            if (AvailableDevices.Any())
                SelectedStreamingDevice = AvailableDevices.First();
        }

        [RelayCommand]
        public void BrowseFolder()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.SelectedPath = RecordingPath;
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    RecordingPath = dialog.SelectedPath;
                }
            }
        }

        [RelayCommand]
        public void Save()
        {
            // First, make sure the current selection's values are captured in the dictionary
            if (SelectedStreamingDevice != null)
            {
                var configs = _configManager.CurrentSettings.DeviceStreamingConfigs;
                if (!configs.ContainsKey(SelectedStreamingDevice.Name))
                {
                    configs[SelectedStreamingDevice.Name] = new StreamingConfig();
                }
                
                var conf = configs[SelectedStreamingDevice.Name];
                conf.Host = StreamHost;
                conf.Port = StreamPort;
                conf.Password = StreamPassword;
                conf.MountPoint = StreamMount;
                conf.Bitrate = StreamBitrate;
                conf.ServerType = StreamServerType;
                conf.SampleRate = StreamSampleRate;
                conf.Channels = StreamChannels;
                conf.Username = StreamUsername;
                conf.Genre = StreamGenre;
            }

            _configManager.CurrentSettings.StationName = StationName;
            _configManager.CurrentSettings.RecordingBasePath = RecordingPath;
            _configManager.CurrentSettings.Mp3Bitrate = Bitrate;
            _configManager.CurrentSettings.SegmentDurationMinutes = SegmentDuration;
            _configManager.CurrentSettings.StartHour = StartHour;
            _configManager.CurrentSettings.EndHour = EndHour;

            _configManager.CurrentSettings.IsSignalREnabled = IsSignalREnabled;
            _configManager.CurrentSettings.SignalRHubUrl = SignalRHubUrl;
            _configManager.CurrentSettings.IsAutoStartEnabled = IsAutoStartEnabled;

            // Apply auto-start to registry
            AutoStartService.SetAutoStart(IsAutoStartEnabled);

            // Save active devices list
            _configManager.CurrentSettings.ActiveInputDevices = AvailableDevices
                .Where(d => d.IsActive)
                .Select(d => d.Name)
                .ToList();

            // Save station name mapping
            _configManager.CurrentSettings.DeviceStationNames = AvailableDevices
                .ToDictionary(d => d.Name, d => d.CustomStationName);

            // Save per-device recording enabled
            _configManager.CurrentSettings.DeviceRecordingEnabled = AvailableDevices
                .ToDictionary(d => d.Name, d => d.IsRecordingEnabled);

            _configManager.Save();
        }
    }
}
