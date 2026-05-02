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
        private readonly PairingService _pairingService;
        private readonly AuthService _authService;
        private static readonly ConcurrentDictionary<string, string> _connectionMap = new();

        // Track which connections are authenticated (API key validated or Dashboard)
        private static readonly ConcurrentDictionary<string, bool> _authenticatedConnections = new();

        // Track which connections are Dashboard (can send commands)
        private static readonly ConcurrentDictionary<string, bool> _dashboardConnections = new();

        private const string DashboardGroup = "dashboard";

        public RadioHub(MonitoringService monitoringService, LicenseManager licenseManager, PairingService pairingService, AuthService authService)
        {
            _monitoringService = monitoringService;
            _licenseManager = licenseManager;
            _pairingService = pairingService;
            _authService = authService;
        }

        /// <summary>
        /// Get the ConnectionId for a specific MachineId to send targeted commands.
        /// </summary>
        public static string? GetConnectionId(string machineId)
        {
            var pair = _connectionMap.FirstOrDefault(x => x.Value == machineId);
            return pair.Key;
        }

        public override async Task OnConnectedAsync()
        {
            var httpContext = Context.GetHttpContext();

            // API key via header only (never query string — avoids key leaking to logs)
            var apiKey = httpContext?.Request.Headers["X-Api-Key"].FirstOrDefault();

            if (!string.IsNullOrEmpty(apiKey) && await _pairingService.ValidateKeyAsync(apiKey))
            {
                _authenticatedConnections[Context.ConnectionId] = true;
                // WPF clients are NOT dashboard — they can only send their own data
            }

            // Dashboard connections: Blazor Server SignalR runs in-process on the same Kestrel.
            var remoteIp = httpContext?.Connection.RemoteIpAddress?.ToString();
            var localPort = httpContext?.Connection.LocalPort;

            if ((remoteIp == "127.0.0.1" || remoteIp == "::1") && localPort == 5046)
            {
                _authenticatedConnections[Context.ConnectionId] = true;
                _dashboardConnections[Context.ConnectionId] = true;
                await Groups.AddToGroupAsync(Context.ConnectionId, DashboardGroup);
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _authenticatedConnections.TryRemove(Context.ConnectionId, out _);
            _dashboardConnections.TryRemove(Context.ConnectionId, out _);
            _pairingService.CleanupConnection(Context.ConnectionId);

            if (_connectionMap.TryRemove(Context.ConnectionId, out var machineId))
            {
                _monitoringService.MarkMachineOffline(machineId);
            }
            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Dashboard clients can explicitly join the dashboard group (for Logs page HubConnection).
        /// </summary>
        public async Task JoinDashboard()
        {
            if (!IsAuthenticated()) return;

            // Only localhost connections can join dashboard (Blazor Server circuits)
            var httpContext = Context.GetHttpContext();
            var remoteIp = httpContext?.Connection.RemoteIpAddress?.ToString();
            var localPort = httpContext?.Connection.LocalPort;

            if ((remoteIp == "127.0.0.1" || remoteIp == "::1") && localPort == 5046)
            {
                _dashboardConnections[Context.ConnectionId] = true;
                await Groups.AddToGroupAsync(Context.ConnectionId, DashboardGroup);
            }
        }

        /// <summary>
        /// Pairing: exchange a 6-digit code for a permanent API key.
        /// This is the ONLY method that doesn't require authentication.
        /// Rate limited to 5 attempts per 5 minutes per connection.
        /// </summary>
        public async Task<PairingResult> Pair(string code, string machineId, string machineName)
        {
            if (_pairingService.IsRateLimited(Context.ConnectionId))
                return new PairingResult { Success = false, Error = "Demasiados intentos. Espera unos minutos." };

            var result = await _pairingService.RedeemCodeAsync(code, machineId, machineName);

            if (result.Success)
            {
                _authenticatedConnections[Context.ConnectionId] = true;

                await _authService.LogActionAsync($"WPF:{machineId}", "PAIRING_COMPLETED",
                    $"Equipo emparejado: {machineName} (Machine: {machineId})");
            }

            return result;
        }

        // ─── AUTHENTICATED METHODS (WPF + Dashboard) ──────────────

        public async Task UpdateStationStatus(StationStatusUpdate update)
        {
            if (!IsAuthenticated()) return;
            if (!ValidateMachineId(update.MachineId)) return;
            _connectionMap[Context.ConnectionId] = update.MachineId;
            await _monitoringService.UpdateStation(update);
            await Clients.Group(DashboardGroup).SendAsync("ReceiveStationUpdate", update);
        }

        public async Task UpdateBatchStatus(BatchStatusUpdate batch)
        {
            if (!IsAuthenticated()) return;
            if (!ValidateMachineId(batch.MachineId)) return;
            _connectionMap[Context.ConnectionId] = batch.MachineId;
            await _monitoringService.UpdateBatch(batch);
            await Clients.Group(DashboardGroup).SendAsync("ReceiveBatchUpdate", batch);

            // License check-in
            _ = _licenseManager.CheckInAsync(batch.MachineId);
        }

        public async Task RegisterMachineInfo(MachineInfo info)
        {
            if (!IsAuthenticated()) return;
            if (!ValidateMachineId(info.MachineId)) return;
            _connectionMap[Context.ConnectionId] = info.MachineId;
            _monitoringService.StoreMachineInfo(info);
            await Clients.Group(DashboardGroup).SendAsync("ReceiveMachineInfo", info);
        }

        public async Task SendLogEntries(LogEntryBatch batch)
        {
            if (!IsAuthenticated()) return;
            if (!ValidateMachineId(batch.MachineId)) return;
            if (batch.Entries.Count == 0) return;
            _connectionMap[Context.ConnectionId] = batch.MachineId;
            await _monitoringService.PersistLogEntries(batch);
        }

        public async Task SendLogFileResponse(LogFileResponse response)
        {
            if (!IsAuthenticated()) return;
            await Clients.Group(DashboardGroup).SendAsync("ReceiveLogFileResponse", response);
        }

        // ─── DASHBOARD-ONLY METHODS ───────────────────────────────

        /// <summary>
        /// Send a command from the Dashboard to a specific WPF client.
        /// Only Dashboard connections can invoke this.
        /// </summary>
        public async Task SendCommandToStation(StationCommand command)
        {
            if (!IsDashboard()) return;
            var connectionId = GetConnectionId(command.MachineId);
            if (connectionId != null)
            {
                await Clients.Client(connectionId).SendAsync("ReceiveCommand", command);
            }
        }

        // ─── HELPERS ──────────────────────────────────────────────

        private bool IsAuthenticated()
        {
            return _authenticatedConnections.ContainsKey(Context.ConnectionId);
        }

        private bool IsDashboard()
        {
            return _dashboardConnections.ContainsKey(Context.ConnectionId);
        }

        /// <summary>
        /// Validates that a WPF client can only send data for its own MachineId.
        /// Dashboard connections can send for any MachineId (they relay commands).
        /// First call from a WPF client sets its MachineId; subsequent calls must match.
        /// </summary>
        private bool ValidateMachineId(string machineId)
        {
            // Dashboard can act on behalf of any machine
            if (IsDashboard()) return true;

            // WPF: first call registers the MachineId, subsequent calls must match
            if (_connectionMap.TryGetValue(Context.ConnectionId, out var registeredId))
            {
                return registeredId == machineId;
            }

            // First call — allow and register
            return true;
        }
    }
}
