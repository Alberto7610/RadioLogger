using Microsoft.EntityFrameworkCore;
using RadioLogger.Shared.Models;
using RadioLogger.Web.Data;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RadioLogger.Web.Services
{
    public class MonitoringService
    {
        private readonly ConcurrentDictionary<string, StationStatusUpdate> _stations = new();
        private readonly ConcurrentDictionary<string, int> _activeIncidentIds = new();
        private readonly IServiceProvider _serviceProvider;

        public event Action? OnUpdated;
        public bool IsDatabaseHealthy { get; set; } = true;

        public MonitoringService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void UpdateStation(StationStatusUpdate update)
        {
            string key = $"{update.MachineId}:{update.StationName}";
            _stations.TryGetValue(key, out var previous);
            _stations[key] = update;

            // Detect Silence Transitions
            if (previous != null)
            {
                if (update.IsSilence && !previous.IsSilence)
                {
                    _ = LogIncidentStart(update, "SILENCE");
                }
                else if (!update.IsSilence && previous.IsSilence)
                {
                    _ = LogIncidentEnd(key);
                }
            }

            OnUpdated?.Invoke();
        }

        public void UpdateBatch(BatchStatusUpdate batch)
        {
            foreach (var station in batch.Stations)
            {
                // Ensure MachineId is set from the batch if not set in individual station
                if (string.IsNullOrEmpty(station.MachineId))
                    station.MachineId = batch.MachineId;
                    
                UpdateStation(station);
            }
        }

        public List<StationStatusUpdate> GetActiveStations()
        {
            // Filter out stations that haven't sent data in 10 seconds (fail-safe)
            var threshold = DateTime.UtcNow.AddSeconds(-10);
            return _stations.Values
                .Where(s => s.Timestamp > threshold)
                .OrderBy(s => s.MachineId)
                .ThenBy(s => s.StationName)
                .ToList();
        }

        public void MarkMachineOffline(string machineId)
        {
            var keysToRemove = _stations.Keys.Where(k => k.StartsWith($"{machineId}:")).ToList();
            foreach (var key in keysToRemove)
            {
                if (_stations.TryRemove(key, out var station))
                {
                    // If it was in silence, end the incident
                    _ = LogIncidentEnd(key);
                }
            }
            OnUpdated?.Invoke();
        }

        private async Task LogIncidentStart(StationStatusUpdate update, string type)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();

                var log = new IncidentLog
                {
                    StationName = update.StationName,
                    MachineId = update.MachineId,
                    EventType = type,
                    StartTime = DateTime.UtcNow,
                    IsResolved = false
                };

                db.Incidents.Add(log);
                await db.SaveChangesAsync();

                string key = $"{update.MachineId}:{update.StationName}";
                _activeIncidentIds[key] = log.Id;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DB Error Start: {ex.Message}");
            }
        }

        private async Task LogIncidentEnd(string key)
        {
            try
            {
                if (_activeIncidentIds.TryRemove(key, out int logId))
                {
                    using var scope = _serviceProvider.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();

                    var log = await db.Incidents.FindAsync(logId);
                    if (log != null)
                    {
                        log.EndTime = DateTime.UtcNow;
                        log.DurationSeconds = (log.EndTime.Value - log.StartTime).TotalSeconds;
                        log.IsResolved = true;
                        await db.SaveChangesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DB Error End: {ex.Message}");
            }
        }
    }
}
