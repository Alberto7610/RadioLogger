using Microsoft.AspNetCore.SignalR;
using RadioLogger.Shared.Models;
using RadioLogger.Web.Services;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace RadioLogger.Web.Hubs
{
    public class RadioHub : Hub
    {
        private readonly MonitoringService _monitoringService;
        private readonly LicenseManager _licenseManager;
        private static readonly ConcurrentDictionary<string, string> _connectionMap = new();

        public RadioHub(MonitoringService monitoringService, LicenseManager licenseManager)
        {
            _monitoringService = monitoringService;
            _licenseManager = licenseManager;
        }

        /// <summary>
        /// Get the ConnectionId for a specific MachineId to send targeted commands.
        /// </summary>
        public static string? GetConnectionId(string machineId)
        {
            return _connectionMap.FirstOrDefault(x => x.Value == machineId).Key;
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (_connectionMap.TryRemove(Context.ConnectionId, out var machineId))
            {
                _monitoringService.MarkMachineOffline(machineId);
            }
            await base.OnDisconnectedAsync(exception);
        }

        public async Task UpdateStationStatus(StationStatusUpdate update)
        {
            _connectionMap[Context.ConnectionId] = update.MachineId;
            await _monitoringService.UpdateStation(update);
            await Clients.All.SendAsync("ReceiveStationUpdate", update);
        }

        public async Task UpdateBatchStatus(BatchStatusUpdate batch)
        {
            _connectionMap[Context.ConnectionId] = batch.MachineId;
            await _monitoringService.UpdateBatch(batch);
            await Clients.All.SendAsync("ReceiveBatchUpdate", batch);

            // License check-in (fire and forget, no bloquear el batch)
            _ = _licenseManager.CheckInAsync(batch.MachineId);
        }

        /// <summary>
        /// Send a command from the Dashboard to a specific WPF client.
        /// Called by MonitoringService via IHubContext.
        /// </summary>
        public async Task SendCommandToStation(StationCommand command)
        {
            var connectionId = GetConnectionId(command.MachineId);
            if (connectionId != null)
            {
                await Clients.Client(connectionId).SendAsync("ReceiveCommand", command);
            }
        }

        /// <summary>
        /// Receive static machine info (sent once on connect/reconnect).
        /// </summary>
        public async Task RegisterMachineInfo(MachineInfo info)
        {
            _connectionMap[Context.ConnectionId] = info.MachineId;
            _monitoringService.StoreMachineInfo(info);
            await Clients.All.SendAsync("ReceiveMachineInfo", info);
        }

        /// <summary>
        /// Receive log entries from WPF clients and persist to DB.
        /// </summary>
        public async Task SendLogEntries(LogEntryBatch batch)
        {
            if (batch.Entries.Count == 0) return;
            _connectionMap[Context.ConnectionId] = batch.MachineId;
            await _monitoringService.PersistLogEntries(batch);
        }

        /// <summary>
        /// WPF client responds with log file content (for dates older than 15 days).
        /// </summary>
        public async Task SendLogFileResponse(LogFileResponse response)
        {
            // Forward to all dashboard clients (they filter by MachineId + Date)
            await Clients.All.SendAsync("ReceiveLogFileResponse", response);
        }
    }
}
