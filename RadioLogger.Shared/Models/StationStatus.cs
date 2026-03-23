using System;

namespace RadioLogger.Shared.Models
{
    /// <summary>
    /// Update for a single radio station.
    /// This object is sent from RadioLogger (WPF) to the Hub.
    /// </summary>
    public class StationStatusUpdate
    {
        public string MachineId { get; set; } = Environment.MachineName;
        public string StationName { get; set; } = string.Empty; // Custom display name
        public string HardwareName { get; set; } = string.Empty; // Stable hardware name (e.g. "Line 1")
        
        // Audio levels (0.0 to 1.0)
        public double LeftLevel { get; set; }
        public double RightLevel { get; set; }
        public double LeftPeak { get; set; }
        public double RightPeak { get; set; }
        
        // Status Flags
        public bool IsRecording { get; set; }
        public DateTime? RecordingStartTime { get; set; }
        public bool IsStreaming { get; set; }
        public string? StreamUrl { get; set; } // URL for the web player
        public bool IsSilence { get; set; }
        public bool IsRecordingEnabled { get; set; } = true; // false = streaming-only mode

        // Licensing & Security
        public string LicenseKey { get; set; } = "FREE-TRIAL";
        public string HardwareId { get; set; } = "UNKNOWN";
        public bool IsAuthorized { get; set; } = false;

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        // Display fields (populated from RegisteredStation DB, not sent by WPF)
        public string? Siglas { get; set; }
        public string? Frecuencia { get; set; }
        public string? Banda { get; set; }
        public string? NombreComercial { get; set; }
        public string? Estado { get; set; }
        public string? Plaza { get; set; }
        public string? GrupoEmpresa { get; set; }
        public string? Formato { get; set; }
    }

    /// <summary>
    /// Permanent record of a station in the Database.
    /// This allows the Dashboard to show offline stations.
    /// </summary>
    public class RegisteredStation
    {
        public int Id { get; set; }
        public string MachineId { get; set; } = string.Empty;
        public string StationName { get; set; } = string.Empty;
        public string HardwareName { get; set; } = string.Empty;
        public string LicenseKey { get; set; } = string.Empty;
        public string HardwareId { get; set; } = string.Empty;
        public bool IsAuthorized { get; set; } = false;
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;
        public bool IsOnline { get; set; } = false;

        // Station identity
        public string? Siglas { get; set; }
        public string? Frecuencia { get; set; }
        public string? Banda { get; set; }
        public string? NombreComercial { get; set; }

        // Location
        public string? Estado { get; set; }
        public string? Plaza { get; set; }

        // Commercial
        public string? GrupoEmpresa { get; set; }
        public string? Formato { get; set; }
        public string? Potencia { get; set; }
        public string? Cobertura { get; set; }

        // Admin
        public string? Notas { get; set; }
        public bool Activa { get; set; } = true;
        public DateTime FechaAlta { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// License management for clients.
    /// </summary>
    public class License
    {
        public int Id { get; set; }
        public string Key { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public int MaxSlots { get; set; } = 1;
        public DateTime ExpirationDate { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// Batch update for multiple stations from a single machine.
    /// Used for performance optimization in SignalR.
    /// </summary>
    public class BatchStatusUpdate
    {
        public string MachineId { get; set; } = Environment.MachineName;
        public System.Collections.Generic.List<StationStatusUpdate> Stations { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Remote command sent from Dashboard to WPF client.
    /// </summary>
    public class StationCommand
    {
        public string MachineId { get; set; } = string.Empty;
        public string HardwareName { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty; // START_RECORDING, STOP_RECORDING, START_STREAMING, STOP_STREAMING
        public string? Payload { get; set; } // Optional data for commands like RESET_PASSWORD
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Historic record of a silence or disconnection incident.
    /// </summary>
    public class IncidentLog
    {
        public int Id { get; set; }
        public string StationName { get; set; } = string.Empty;
        public string MachineId { get; set; } = string.Empty;
        
        public string EventType { get; set; } = "SILENCE"; // SILENCE, DISCONNECT
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        
        public double? DurationSeconds { get; set; }
        public bool IsResolved { get; set; }
    }
}
