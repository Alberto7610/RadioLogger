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
        public string Key { get; set; } = string.Empty;         // RL-2026-XXXX-XXXX
        public string LicenseType { get; set; } = "DEMO";       // DEMO, ESTACION, PLAZA
        public string ClientName { get; set; } = string.Empty;
        public string MachineId { get; set; } = string.Empty;   // Vinculada a un equipo
        public string HardwareId { get; set; } = string.Empty;  // Vinculada a hardware
        public int MaxSlots { get; set; } = 4;                  // Máx estaciones permitidas
        public DateTime StartDate { get; set; } = DateTime.UtcNow;
        public DateTime ExpirationDate { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime? LastCheckIn { get; set; }               // Último check del WPF
        public string? Notes { get; set; }
    }

    /// <summary>
    /// License data stored locally in WPF for offline validation.
    /// </summary>
    public class LocalLicense
    {
        public string Key { get; set; } = string.Empty;
        public string LicenseType { get; set; } = string.Empty;
        public int MaxSlots { get; set; } = 0;
        public DateTime ExpirationDate { get; set; }
        public DateTime LastServerCheck { get; set; }            // Última vez que el servidor confirmó la licencia
        public bool IsValid { get; set; }
    }

    /// <summary>
    /// Batch update for multiple stations from a single machine.
    /// Used for performance optimization in SignalR.
    /// </summary>
    public class BatchStatusUpdate
    {
        public string MachineId { get; set; } = Environment.MachineName;
        public System.Collections.Generic.List<StationStatusUpdate> Stations { get; set; } = new();
        public MachineMetrics? Metrics { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Static info about the machine. Sent once on connect/reconnect.
    /// </summary>
    public class MachineInfo
    {
        public string MachineId { get; set; } = Environment.MachineName;
        public string AppVersion { get; set; } = string.Empty;
        public bool AutoLoginEnabled { get; set; }
        public bool AutoStartEnabled { get; set; }
        public string LocalIp { get; set; } = string.Empty;
        public string PublicIp { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Dynamic machine resource metrics. Sent with every batch update.
    /// </summary>
    public class MachineMetrics
    {
        public double DiskFreeGb { get; set; }
        public double DiskTotalGb { get; set; }
        public double CpuPercent { get; set; }
        public double RamUsedGb { get; set; }
        public double RamTotalGb { get; set; }
        public double WindowsUptimeHours { get; set; }
        public double AppUptimeHours { get; set; }
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

    // ─── USERS & AUDIT ──────────────────────────────

    public class AppUser
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;        // Obligatorio — para recuperación de contraseña
        public string Role { get; set; } = "Operador";          // Administrador, Supervisor, Operador
        public bool IsActive { get; set; } = true;
        public string? TelegramChatId { get; set; }              // Opcional — método alterno de recuperación
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastLogin { get; set; }
    }

    public class AuditEntry
    {
        public long Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;       // LOGIN, LOGOUT, COMMAND, LICENSE_CREATE, etc.
        public string? Detail { get; set; }
        public string? IpAddress { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    // ─── LOGS ──────────────────────────────────────

    /// <summary>
    /// Log entry sent from WPF to Dashboard for centralized viewing.
    /// </summary>
    public class LogEntry
    {
        public long Id { get; set; }
        public string MachineId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = "INF";      // INF, WRN, ERR, FTL, DBG
        public string Source { get; set; } = string.Empty; // SourceContext (AudioChannel, SignalRService, etc.)
        public string Message { get; set; } = string.Empty;
        public string? Exception { get; set; }
    }

    /// <summary>
    /// Batch of log entries sent from WPF via SignalR.
    /// </summary>
    public class LogEntryBatch
    {
        public string MachineId { get; set; } = string.Empty;
        public System.Collections.Generic.List<LogEntry> Entries { get; set; } = new();
    }

    /// <summary>
    /// Request for log file content from a specific machine (days 16-30).
    /// </summary>
    public class LogFileRequest
    {
        public string MachineId { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty; // yyyy-MM-dd
    }

    /// <summary>
    /// Response with log file content from a WPF machine.
    /// </summary>
    public class LogFileResponse
    {
        public string MachineId { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public System.Collections.Generic.List<string> Lines { get; set; } = new();
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

    /// <summary>
    /// API Key for authenticating SignalR WPF clients.
    /// Created via pairing code flow.
    /// </summary>
    public class ApiKey
    {
        public int Id { get; set; }
        public string Key { get; set; } = string.Empty;
        public string MachineId { get; set; } = string.Empty;
        public string MachineName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// Result of a pairing operation sent from Hub to WPF.
    /// </summary>
    public class PairingResult
    {
        public bool Success { get; set; }
        public string? ApiKey { get; set; }
        public string? Error { get; set; }
    }
}
