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
        public string StationName { get; set; } = string.Empty;
        
        // Audio levels (0.0 to 1.0)
        public double LeftLevel { get; set; }
        public double RightLevel { get; set; }
        
        // Status Flags
        public bool IsRecording { get; set; }
        public bool IsStreaming { get; set; }
        public bool IsSilence { get; set; }
        
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
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
}
