using Microsoft.AspNetCore.SignalR;
using RadioLogger.Shared.Models;
using RadioLogger.Web.Services;
using System.Threading.Tasks;

namespace RadioLogger.Web.Hubs
{
    public class RadioHub : Hub
    {
        private readonly MonitoringService _monitoringService;

        public RadioHub(MonitoringService monitoringService)
        {
            _monitoringService = monitoringService;
        }

        public async Task UpdateStationStatus(StationStatusUpdate update)
        {
            _monitoringService.UpdateStation(update);
            await Clients.All.SendAsync("ReceiveStationUpdate", update);
        }

        public async Task UpdateBatchStatus(BatchStatusUpdate batch)
        {
            _monitoringService.UpdateBatch(batch);
            await Clients.All.SendAsync("ReceiveBatchUpdate", batch);
        }
    }
}
