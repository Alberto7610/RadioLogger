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
        public string SignalRApiKey { get; set; } = string.Empty; // Assigned via pairing code

        public bool IsAutoStartEnabled { get; set; } = false;

        // Windows Auto-Login
        public bool IsAutoLoginEnabled { get; set; } = false;
        public string AutoLoginUsername { get; set; } = "";

        // Per-device recording enabled (if false for a device, it only streams without recording to disk)
        public Dictionary<string, bool> DeviceRecordingEnabled { get; set; } = new Dictionary<string, bool>();

        // Settings for auto-start recording/streaming on launch
        public List<string> AutoRecordDevices { get; set; } = new List<string>();
        public List<string> AutoStreamDevices { get; set; } = new List<string>();

        // Security: BCrypt hash of settings password (default: "admin")
        // Legacy SHA256 hashes (64 hex chars) are auto-migrated to BCrypt on first login
        public string SettingsPasswordHash { get; set; } = "8c6976e5b5410415bde908bd4dee15dfb167a9c873fc4bb8a81f6f2ab448a918";

        // License (stored locally for offline validation)
        public RadioLogger.Shared.Models.LocalLicense? License { get; set; }
    }
}
