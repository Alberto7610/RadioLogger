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

        public MonitoringService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void UpdateStation(StationStatusUpdate update)
        {
            _stations.TryGetValue(update.StationName, out var previous);
            _stations[update.StationName] = update;

            // Detect Silence Transitions
            if (previous != null)
            {
                if (update.IsSilence && !previous.IsSilence)
                {
                    // START of Silence
                    _ = LogIncidentStart(update, "SILENCE");
                }
                else if (!update.IsSilence && previous.IsSilence)
                {
                    // END of Silence
                    _ = LogIncidentEnd(update.StationName);
                }
            }
        }

        public void UpdateBatch(BatchStatusUpdate batch)
        {
            foreach (var station in batch.Stations)
            {
                UpdateStation(station);
            }
        }

        public List<StationStatusUpdate> GetActiveStations()
        {
            return _stations.Values.OrderBy(s => s.StationName).ToList();
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

                _activeIncidentIds[update.StationName] = log.Id;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DB Error Start: {ex.Message}");
            }
        }

        private async Task LogIncidentEnd(string stationName)
        {
            try
            {
                if (_activeIncidentIds.TryRemove(stationName, out int logId))
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
