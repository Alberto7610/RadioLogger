using RadioLogger.Web.Services;
using System.Collections.Concurrent;

namespace RadioLogger.Web.Services
{
    public class WatchdogService : BackgroundService
    {
        private readonly MonitoringService _monitoring;
        private readonly TelegramService _telegram;
        private readonly ConcurrentDictionary<string, DateTime> _offlineAlerts = new();
        private DateTime _lastLogCleanup = DateTime.MinValue;

        public WatchdogService(MonitoringService monitoring, TelegramService telegram)
        {
            _monitoring = monitoring;
            _telegram = telegram;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Esperar un poco al inicio para que las estaciones se registren
            await Task.Delay(10000, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var allStations = _monitoring.GetAllRawStations();
                var now = DateTime.UtcNow;

                foreach (var station in allStations)
                {
                    string key = $"{station.MachineId}:{station.StationName}";
                    
                    // Si no ha reportado en más de 15 segundos
                    if ((now - station.Timestamp).TotalSeconds > 15)
                    {
                        // Solo enviar alerta si no hemos avisado ya de esta desconexión
                        if (!_offlineAlerts.ContainsKey(key))
                        {
                            _offlineAlerts[key] = now;
                            await _telegram.SendAlertAsync(
                                $"🚨 <b>EQUIPO DESCONECTADO</b>\n" +
                                $"Servidor: <b>{station.MachineId}</b>\n" +
                                $"Estación: <b>{station.StationName}</b>\n" +
                                $"Estado: No se reciben datos de SignalR (¿Sin Internet?).");
                            
                            // También marcarla como offline en el servicio de monitoreo para que el Dashboard lo refleje
                            _monitoring.MarkStationOfflineExplicitly(key);
                        }
                    }
                    else
                    {
                        // Si volvió a reportar, limpiar la alerta previa
                        if (_offlineAlerts.TryRemove(key, out _))
                        {
                            await _telegram.SendAlertAsync(
                                $"✅ <b>EQUIPO RESTABLECIDO</b>\n" +
                                $"Servidor: <b>{station.MachineId}</b>\n" +
                                $"Estación: <b>{station.StationName}</b>\n" +
                                $"Estado: Conexión SignalR recuperada.");
                        }
                    }
                }

                // Cleanup de logs viejos cada hora
                if ((DateTime.UtcNow - _lastLogCleanup).TotalHours >= 1)
                {
                    await _monitoring.CleanupOldLogEntries();
                    _lastLogCleanup = DateTime.UtcNow;
                }

                await Task.Delay(5000, stoppingToken); // Revisar cada 5s
            }
        }
    }
}