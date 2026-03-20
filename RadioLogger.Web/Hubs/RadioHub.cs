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
        private static readonly ConcurrentDictionary<string, string> _connectionMap = new();

        public RadioHub(MonitoringService monitoringService)
        {
            _monitoringService = monitoringService;
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
    }
}
