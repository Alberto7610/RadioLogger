using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioLogger.Models;
using RadioLogger.Services;
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
            StartHour = _configManager.CurrentSettings.StartHour;
            EndHour = _configManager.CurrentSettings.EndHour;

            IsSignalREnabled = _configManager.CurrentSettings.IsSignalREnabled;
            SignalRHubUrl = _configManager.CurrentSettings.SignalRHubUrl;
            IsAutoStartEnabled = _configManager.CurrentSettings.IsAutoStartEnabled;

            LoadDevices();
        }

        private void LoadDevices()
        {
            var systemDevices = _audioEngine.GetInputDevices();
            var savedActive = _configManager.CurrentSettings.ActiveInputDevices;
            var nameMapping = _configManager.CurrentSettings.DeviceStationNames;

            foreach (var dev in systemDevices)
            {
                if (!nameMapping.TryGetValue(dev.Name, out string? customName) || string.IsNullOrWhiteSpace(customName))
                {
                    customName = dev.Name;
                }

                AvailableDevices.Add(new DeviceSelection
                {
                    Name = dev.Name,
                    Driver = dev.Driver,
                    IsActive = savedActive.Contains(dev.Name),
                    CustomStationName = customName
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

            _configManager.Save();
        }
    }
}
