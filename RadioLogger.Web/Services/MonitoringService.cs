using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RadioLogger.Shared.Models;
using RadioLogger.Web.Data;
using RadioLogger.Web.Hubs;
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

        // Cache para evitar saturar la DB
        private List<RegisteredStation> _registeredCache = new();
        private DateTime _lastCacheUpdate = DateTime.MinValue;

        public event Action? OnUpdated;
        public bool IsDatabaseHealthy { get; set; } = true;

        public MonitoringService(IServiceProvider serviceProvider, TelegramService telegram)
        {
            _serviceProvider = serviceProvider;
            _telegram = telegram;
            _ = RefreshCache(); // Carga inicial
        }

        /// <summary>
        /// Send a remote command to a WPF client via SignalR.
        /// </summary>
        public async Task SendCommandAsync(StationCommand command)
        {
            try
            {
                var hubContext = _serviceProvider.GetRequiredService<IHubContext<RadioHub>>();
                var connectionId = RadioHub.GetConnectionId(command.MachineId);
                if (connectionId != null)
                {
                    await hubContext.Clients.Client(connectionId).SendAsync("ReceiveCommand", command);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MONITORING] Error SendCommand: {ex.Message}");
            }
        }

        private async Task RefreshCache()
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
                _registeredCache = await db.RegisteredStations.ToListAsync();
                _lastCacheUpdate = DateTime.Now;
                IsDatabaseHealthy = true;
            }
            catch (Exception ex)
            {
                IsDatabaseHealthy = false;
                Console.WriteLine($"[MONITORING] Error RefreshCache: {ex.Message}");
            }
        }

        public async Task UpdateStation(StationStatusUpdate update)
        {
            await UpdateStationInternal(update);
            OnUpdated?.Invoke();
        }

        private async Task UpdateStationInternal(StationStatusUpdate update)
        {
            // Use HardwareName as stable key to allow renaming StationName in Dashboard
            string key = $"{update.MachineId}:{update.HardwareName}";
            bool isNew = !_stations.ContainsKey(key);
            
            _stations.TryGetValue(key, out var previous);
            _stations[key] = update;

            // Sync to DB immediately if new, or every 10 seconds to update LastSeen
            if (isNew || update.Timestamp.Second % 10 == 0)
            {
                _ = SyncStationToDb(update);
            }

            // Detect Silence Transitions
            if (previous != null)
            {
                if (update.IsSilence && !previous.IsSilence)
                {
                    _ = LogIncidentStart(update, "SILENCE");
                    _ = _telegram.SendAlertAsync($"🔴 <b>ALERTA DE SILENCIO</b>\nServidor: <b>{update.MachineId}</b>\nEstación: <b>{update.StationName}</b>");
                }
                else if (!update.IsSilence && previous.IsSilence)
                {
                    _ = LogIncidentEnd(key);
                    _ = _telegram.SendAlertAsync($"🟢 <b>AUDIO RESTABLECIDO</b>\nServidor: <b>{update.MachineId}</b>\nEstación: <b>{update.StationName}</b>");
                }
            }
        }

        public async Task UpdateBatch(BatchStatusUpdate batch)
        {
            foreach (var station in batch.Stations)
            {
                if (string.IsNullOrEmpty(station.MachineId))
                    station.MachineId = batch.MachineId;
                
                // Ensure HardwareName is set (fallback to StationName if old client)
                if (string.IsNullOrEmpty(station.HardwareName))
                    station.HardwareName = station.StationName;
                    
                await UpdateStationInternal(station);
            }
            OnUpdated?.Invoke(); // Una sola notificación para todo el grupo
        }

        private async Task SyncStationToDb(StationStatusUpdate update)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();

                // Look up by MachineId and HardwareName (the stable key)
                var station = await db.RegisteredStations
                    .FirstOrDefaultAsync(s => s.MachineId == update.MachineId && s.HardwareName == update.HardwareName);

                if (station == null)
                {
                    station = new RegisteredStation
                    {
                        MachineId = update.MachineId,
                        HardwareName = update.HardwareName,
                        StationName = update.StationName, // Use initial name
                        HardwareId = update.HardwareId,
                        IsAuthorized = false,
                        IsOnline = true,
                        LastSeen = DateTime.UtcNow
                    };
                    db.RegisteredStations.Add(station);
                    await db.SaveChangesAsync();
                    await RefreshCache();
                }
                else
                {
                    // Update last seen
                    station.LastSeen = DateTime.UtcNow;
                    station.IsOnline = true;
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MONITORING] Error SyncStationToDb: {ex.Message}");
            }
        }

        public async Task AuthorizeStation(int id, bool authorized)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
            var station = await db.RegisteredStations.FindAsync(id);
            if (station != null)
            {
                station.IsAuthorized = authorized;
                await db.SaveChangesAsync();
                await RefreshCache();
                OnUpdated?.Invoke();
            }
        }

        public async Task UpdateStationDisplayName(int id, string newName)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
            var station = await db.RegisteredStations.FindAsync(id);
            if (station != null)
            {
                station.StationName = newName;
                await db.SaveChangesAsync();
                await RefreshCache();
                OnUpdated?.Invoke();
            }
        }

        public async Task UpdateStationDetails(RegisteredStation updated)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
            var station = await db.RegisteredStations.FindAsync(updated.Id);
            if (station != null)
            {
                station.StationName = updated.StationName;
                station.Siglas = updated.Siglas;
                station.Frecuencia = updated.Frecuencia;
                station.Banda = updated.Banda;
                station.NombreComercial = updated.NombreComercial;
                station.Estado = updated.Estado;
                station.Plaza = updated.Plaza;
                station.GrupoEmpresa = updated.GrupoEmpresa;
                station.Formato = updated.Formato;
                station.Potencia = updated.Potencia;
                station.Cobertura = updated.Cobertura;
                station.Notas = updated.Notas;
                station.Activa = updated.Activa;
                await db.SaveChangesAsync();
                await RefreshCache();
                OnUpdated?.Invoke();
            }
        }

        public List<RegisteredStation> GetRegisteredStations()
        {
            return _registeredCache;
        }

        public List<StationStatusUpdate> GetActiveStations()
        {
            var list = new List<StationStatusUpdate>();
            var threshold = DateTime.UtcNow.AddSeconds(-15);
            var registered = _registeredCache; // Usar caché instantánea
            
            // 1. Only show authorized stations
            var handledKeys = new HashSet<string>();
            foreach (var reg in registered.Where(r => r.IsAuthorized))
            {
                string key = $"{reg.MachineId}:{reg.HardwareName}";
                handledKeys.Add(key);
                
                if (_stations.TryGetValue(key, out var live) && live.Timestamp > threshold)
                {
                    live.IsAuthorized = reg.IsAuthorized;
                    live.StationName = reg.StationName; 
                    list.Add(live);
                }
                else
                {
                    list.Add(new StationStatusUpdate
                    {
                        MachineId = reg.MachineId,
                        HardwareName = reg.HardwareName,
                        StationName = reg.StationName,
                        IsAuthorized = reg.IsAuthorized,
                        Timestamp = reg.LastSeen,
                        LeftLevel = 0, RightLevel = 0,
                        IsSilence = false 
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
        public int GetActiveIncidentsCount()
        {
            return _activeIncidentIds.Count;
        }

        public void MarkMachineOffline(string machineId)
        {
            var machineStations = _stations.Values.Where(s => s.MachineId == machineId).ToList();
            foreach (var station in machineStations)
            {
                string key = $"{station.MachineId}:{station.HardwareName}";
                // No la eliminamos de _stations, solo cerramos el incidente si existía.
                // El Watchdog se encargará de ver que el Timestamp es viejo y enviará el Telegram.
                _ = LogIncidentEnd(key);
            }
            OnUpdated?.Invoke();
        }

        public async Task ResetStationPassword(string machineId, string newPasswordHash)
        {
            var command = new StationCommand
            {
                MachineId = machineId,
                Command = "RESET_PASSWORD",
                Payload = newPasswordHash
            };
            await SendCommandAsync(command);
        }

        public async Task DeleteStation(int id)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
            var station = await db.RegisteredStations.FindAsync(id);
            if (station != null)
            {
                db.RegisteredStations.Remove(station);
                await db.SaveChangesAsync();
                await RefreshCache();
                OnUpdated?.Invoke();
            }
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

                string key = $"{update.MachineId}:{update.HardwareName}";
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