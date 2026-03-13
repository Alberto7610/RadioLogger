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
        private readonly TelegramService _telegram;

        public event Action? OnUpdated;
        public bool IsDatabaseHealthy { get; set; } = true;

        public MonitoringService(IServiceProvider serviceProvider, TelegramService telegram)
        {
            _serviceProvider = serviceProvider;
            _telegram = telegram;
        }

        public async Task UpdateStation(StationStatusUpdate update)
        {
            string key = $"{update.MachineId}:{update.StationName}";
            _stations.TryGetValue(key, out var previous);
            _stations[key] = update;

            // Persistent Database Sync
            _ = SyncStationToDb(update);

            // Detect Silence Transitions
            if (previous != null)
            {
                if (update.IsSilence && !previous.IsSilence)
                {
                    _ = LogIncidentStart(update, "SILENCE");
                    
                    _ = _telegram.SendAlertAsync(
                        $"🔴 <b>ALERTA DE SILENCIO</b>\n" +
                        $"Servidor: <b>{update.MachineId}</b>\n" +
                        $"Estación: <b>{update.StationName}</b>\n" +
                        $"Estado: No se detecta audio.");
                }
                else if (!update.IsSilence && previous.IsSilence)
                {
                    _ = LogIncidentEnd(key);
                    
                    _ = _telegram.SendAlertAsync(
                        $"🟢 <b>AUDIO RESTABLECIDO</b>\n" +
                        $"Servidor: <b>{update.MachineId}</b>\n" +
                        $"Estación: <b>{update.StationName}</b>\n" +
                        $"Estado: Audio detectado nuevamente.");
                }
            }

            OnUpdated?.Invoke();
        }

        public async Task UpdateBatch(BatchStatusUpdate batch)
        {
            foreach (var station in batch.Stations)
            {
                if (string.IsNullOrEmpty(station.MachineId))
                    station.MachineId = batch.MachineId;
                    
                await UpdateStation(station);
            }
        }

        private async Task SyncStationToDb(StationStatusUpdate update)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();

                var station = await db.RegisteredStations
                    .FirstOrDefaultAsync(s => s.MachineId == update.MachineId && s.StationName == update.StationName);

                if (station == null)
                {
                    station = new RegisteredStation
                    {
                        MachineId = update.MachineId,
                        StationName = update.StationName,
                        HardwareId = update.HardwareId,
                        LicenseKey = update.LicenseKey,
                        IsAuthorized = false, // Must be authorized manually
                        IsOnline = true,
                        LastSeen = DateTime.UtcNow
                    };
                    db.RegisteredStations.Add(station);
                }
                else
                {
                    station.LastSeen = DateTime.UtcNow;
                    station.IsOnline = true;
                    // Update Hardware ID if changed (migration detection)
                    if (station.HardwareId != update.HardwareId)
                    {
                        station.HardwareId = update.HardwareId;
                        // station.IsAuthorized = false; // Could auto-unauthorize if security is strict
                    }
                }

                await db.SaveChangesAsync();
            }
            catch { }
        }

        public List<StationStatusUpdate> GetActiveStations()
        {
            // Now we can merge RAM (real-time levels) with DB (permanent list)
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
            var registered = db.RegisteredStations.ToList();

            var list = new List<StationStatusUpdate>();
            var threshold = DateTime.UtcNow.AddSeconds(-15);

            foreach (var reg in registered)
            {
                string key = $"{reg.MachineId}:{reg.StationName}";
                if (_stations.TryGetValue(key, out var live) && live.Timestamp > threshold)
                {
                    // Live station
                    live.IsAuthorized = reg.IsAuthorized;
                    list.Add(live);
                }
                else
                {
                    // Offline station from DB
                    list.Add(new StationStatusUpdate
                    {
                        MachineId = reg.MachineId,
                        StationName = reg.StationName,
                        IsRecording = false,
                        IsStreaming = false,
                        IsAuthorized = reg.IsAuthorized,
                        Timestamp = reg.LastSeen, // Show when it was last seen
                        LeftLevel = 0, RightLevel = 0 // Greyed out
                    });
                }
            }

            return list.OrderBy(s => s.MachineId).ThenBy(s => s.StationName).ToList();
        }

        /// <summary>
        /// Used by Watchdog to check all stations regardless of their age
        /// </summary>
        public List<StationStatusUpdate> GetAllRawStations()
        {
            return _stations.Values.ToList();
        }
public void MarkMachineOffline(string machineId)
{
    var machineStations = _stations.Values.Where(s => s.MachineId == machineId).ToList();
    foreach (var station in machineStations)
    {
        string key = $"{station.MachineId}:{station.StationName}";
        // No la eliminamos de _stations, solo cerramos el incidente si existía.
        // El Watchdog se encargará de ver que el Timestamp es viejo y enviará el Telegram.
        _ = LogIncidentEnd(key);
    }
    OnUpdated?.Invoke();
}

        public void MarkStationOfflineExplicitly(string key)
        {
            // We don't remove it here so the Watchdog can still "see" it to check if it recovers.
            // But we can trigger the incident end if it was in silence.
            _ = LogIncidentEnd(key);
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