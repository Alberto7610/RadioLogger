using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioLogger.Models;
using RadioLogger.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Forms; // For FolderBrowserDialog

namespace RadioLogger.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly ConfigManager _configManager;
        private readonly AudioEngine _audioEngine;

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

        [ObservableProperty]
        private bool _enableTelegram;

        [ObservableProperty]
        private string _telegramToken = string.Empty;

        [ObservableProperty]
        private string _telegramChatId = string.Empty;

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

        [ObservableProperty] private string _testStatus = "";
        [ObservableProperty] private string _testStatusColor = "#666"; // Default Grey

        // SignalR
        [ObservableProperty] private bool _isSignalREnabled;
        [ObservableProperty] private string _signalRHubUrl = string.Empty;

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

            string currentHash = RadioLogger.Views.PasswordDialog.ComputeHash(CurrentPassword);
            if (currentHash != _configManager.CurrentSettings.SettingsPasswordHash)
            {
                PasswordStatus = "Contraseña actual incorrecta";
                PasswordStatusColor = "#FF4444";
                return;
            }

            _configManager.CurrentSettings.SettingsPasswordHash = RadioLogger.Views.PasswordDialog.ComputeHash(NewPassword);
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

            await System.Threading.Tasks.Task.Run(() =>
            {
                var tempConfig = new StreamingConfig
                {
                    Host = StreamHost,
                    Port = StreamPort,
                    Password = StreamPassword,
                    MountPoint = StreamMount,
                    ServerType = StreamServerType
                };

                var (success, msg) = _audioEngine.TestConnection(tempConfig);
                
                TestStatus = msg;
                TestStatusColor = success ? "#00FF00" : "#FF0000"; // Green vs Red
            });
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
            }
        }

        // Methods to sync back to config object when UI changes
        partial void OnStreamHostChanged(string value) => UpdateCurrentStreamConfig(c => c.Host = value);
        partial void OnStreamPortChanged(int value) => UpdateCurrentStreamConfig(c => c.Port = value);
        partial void OnStreamPasswordChanged(string value) => UpdateCurrentStreamConfig(c => c.Password = value);
        partial void OnStreamMountChanged(string value) => UpdateCurrentStreamConfig(c => c.MountPoint = value);
        partial void OnStreamBitrateChanged(int value) => UpdateCurrentStreamConfig(c => c.Bitrate = value);
        partial void OnStreamServerTypeChanged(string value) => UpdateCurrentStreamConfig(c => c.ServerType = value);

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

        public SettingsViewModel(ConfigManager configManager, AudioEngine audioEngine)
        {
            _configManager = configManager;
            _audioEngine = audioEngine;

            // Load current values
            StationName = _configManager.CurrentSettings.StationName;
            RecordingPath = _configManager.CurrentSettings.RecordingBasePath;
            Bitrate = _configManager.CurrentSettings.Mp3Bitrate;
            SegmentDuration = _configManager.CurrentSettings.SegmentDurationMinutes;
            StartHour = _configManager.CurrentSettings.StartHour;
            EndHour = _configManager.CurrentSettings.EndHour;

            EnableTelegram = _configManager.CurrentSettings.EnableTelegram;
            TelegramToken = _configManager.CurrentSettings.TelegramToken;
            TelegramChatId = _configManager.CurrentSettings.TelegramChatId;

            IsSignalREnabled = _configManager.CurrentSettings.IsSignalREnabled;
            SignalRHubUrl = _configManager.CurrentSettings.SignalRHubUrl;
            IsAutoStartEnabled = _configManager.CurrentSettings.IsAutoStartEnabled;
            IsAutoLoginEnabled = AutoStartService.IsAutoLoginEnabled();
            if (!string.IsNullOrEmpty(_configManager.CurrentSettings.AutoLoginUsername))
                AutoLoginUsername = _configManager.CurrentSettings.AutoLoginUsername;

            LoadDevices();
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
            }

            _configManager.CurrentSettings.StationName = StationName;
            _configManager.CurrentSettings.RecordingBasePath = RecordingPath;
            _configManager.CurrentSettings.Mp3Bitrate = Bitrate;
            _configManager.CurrentSettings.SegmentDurationMinutes = SegmentDuration;
            _configManager.CurrentSettings.StartHour = StartHour;
            _configManager.CurrentSettings.EndHour = EndHour;

            _configManager.CurrentSettings.EnableTelegram = EnableTelegram;
            _configManager.CurrentSettings.TelegramToken = TelegramToken;
            _configManager.CurrentSettings.TelegramChatId = TelegramChatId;

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
