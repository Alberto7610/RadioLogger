using System.Collections.Generic;
using System.IO;

namespace RadioLogger.Models
{
    public class AppSettings
    {
        public string StationName { get; set; } = "Radio Estación 1";
        public string RecordingBasePath { get; set; } = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "RadioLogger");
        public int Mp3Bitrate { get; set; } = 128; // Kbps
        public int SegmentDurationMinutes { get; set; } = 60; // Default 1 hour
        
        // Recording Schedule (0-23)
        public int StartHour { get; set; } = 0; // 0 = Medianoche
        public int EndHour { get; set; } = 24; // 24 = Todo el día
        
        public string HeartbeatUrl { get; set; } = "https://api.tuservidor.com/heartbeat";
        public int HeartbeatIntervalSeconds { get; set; } = 60;
        
        // List of Device Names that are ENABLED to appear in the dashboard
        // If empty, show all available inputs.
        public List<string> ActiveInputDevices { get; set; } = new List<string>();

        // Map: Hardware Device Name -> Custom Station Name
        public Dictionary<string, string> DeviceStationNames { get; set; } = new Dictionary<string, string>();

        // Map: Hardware Device Name -> Streaming Configuration
        public Dictionary<string, StreamingConfig> DeviceStreamingConfigs { get; set; } = new Dictionary<string, StreamingConfig>();
        
        // SignalR Remote Monitoring
        public bool IsSignalREnabled { get; set; } = true;
        public string SignalRHubUrl { get; set; } = "http://127.0.0.1:5046/radiohub";
        public int SignalRUpdateIntervalMs { get; set; } = 200; // 5 FPS for remote meters

        // Telegram Notifications
        public bool EnableTelegram { get; set; } = false;
        public string TelegramToken { get; set; } = string.Empty;
        public string TelegramChatId { get; set; } = string.Empty;

        public bool IsAutoStartEnabled { get; set; } = false;

        // Per-device recording enabled (if false for a device, it only streams without recording to disk)
        public Dictionary<string, bool> DeviceRecordingEnabled { get; set; } = new Dictionary<string, bool>();

        // Settings for auto-start recording/streaming on launch
        public List<string> AutoRecordDevices { get; set; } = new List<string>();
        public List<string> AutoStreamDevices { get; set; } = new List<string>();

        // Security: SHA256 hash of settings password (default: hash of "admin")
        public string SettingsPasswordHash { get; set; } = "8c6976e5b5410415bde908bd4dee15dfb167a9c873fc4bb8a81f6f2ab448a918";
    }
}
