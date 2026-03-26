using RadioLogger.Web.Services;
using System.Collections.Concurrent;

namespace RadioLogger.Web.Services
{
    public class WatchdogService : BackgroundService
    {
        private readonly MonitoringService _monitoring;
        private readonly TelegramService _telegram;
        private readonly ConcurrentDictionary<string, DateTime> _offlineAlerts = new();
        private readonly ConcurrentDictionary<string, DateTime> _cpuHighStart = new();
        private readonly ConcurrentDictionary<string, bool> _cpuAlertSent = new();
        private readonly ConcurrentDictionary<string, bool> _diskAlertSent = new();
        private readonly ConcurrentDictionary<string, bool> _ramAlertSent = new();
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

                // Revisar métricas de recursos por máquina
                foreach (var machineId in _monitoring.GetAllMachineIds())
                {
                    var metrics = _monitoring.GetMachineMetrics(machineId);
                    if (metrics == null) continue;

                    // Disco < 10GB libre
                    if (metrics.DiskFreeGb < 10 && !_diskAlertSent.ContainsKey(machineId))
                    {
                        _diskAlertSent[machineId] = true;
                        await _telegram.SendAlertAsync(
                            $"⚠️ <b>DISCO BAJO</b>\nEquipo: <b>{machineId}</b>\nLibre: {metrics.DiskFreeGb:F1} GB");
                    }
                    else if (metrics.DiskFreeGb >= 10)
                    {
                        _diskAlertSent.TryRemove(machineId, out _);
                    }

                    // CPU > 90% sostenido 5 minutos
                    if (metrics.CpuPercent > 90)
                    {
                        if (!_cpuHighStart.ContainsKey(machineId))
                            _cpuHighStart[machineId] = DateTime.UtcNow;
                        else if ((DateTime.UtcNow - _cpuHighStart[machineId]).TotalMinutes >= 5 && !_cpuAlertSent.ContainsKey(machineId))
                        {
                            _cpuAlertSent[machineId] = true;
                            await _telegram.SendAlertAsync(
                                $"🔴 <b>CPU CRITICO</b>\nEquipo: <b>{machineId}</b>\nCPU: {metrics.CpuPercent:F0}% por más de 5 min");
                        }
                    }
                    else
                    {
                        _cpuHighStart.TryRemove(machineId, out _);
                        _cpuAlertSent.TryRemove(machineId, out _);
                    }

                    // RAM > 90%
                    double ramPct = metrics.RamTotalGb > 0 ? (metrics.RamUsedGb / metrics.RamTotalGb * 100) : 0;
                    if (ramPct > 90 && !_ramAlertSent.ContainsKey(machineId))
                    {
                        _ramAlertSent[machineId] = true;
                        await _telegram.SendAlertAsync(
                            $"🔴 <b>RAM CRITICA</b>\nEquipo: <b>{machineId}</b>\nRAM: {metrics.RamUsedGb:F1}/{metrics.RamTotalGb:F1} GB ({ramPct:F0}%)");
                    }
                    else if (ramPct <= 90)
                    {
                        _ramAlertSent.TryRemove(machineId, out _);
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