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
            _monitoringService.UpdateStation(update);
            await Clients.All.SendAsync("ReceiveStationUpdate", update);
        }

        public async Task UpdateBatchStatus(BatchStatusUpdate batch)
        {
            _connectionMap[Context.ConnectionId] = batch.MachineId;
            _monitoringService.UpdateBatch(batch);
            await Clients.All.SendAsync("ReceiveBatchUpdate", batch);
        }
    }
}
