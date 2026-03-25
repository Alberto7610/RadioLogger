using Microsoft.AspNetCore.SignalR.Client;
using RadioLogger.Models;
using RadioLogger.Shared.Models; // Import Shared Models
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RadioLogger.Services
{
    public class SignalRService : IDisposable
    {
        private HubConnection? _connection;
        private readonly AppSettings _settings;
        private bool _isDisposed;

        public event Action<string, bool>? ConnectionStatusChanged;
        public event Action<RadioLogger.Shared.Models.StationCommand>? CommandReceived;

        public bool IsConnected => _connection?.State == HubConnectionState.Connected;

        public SignalRService(AppSettings settings)
        {
            _settings = settings;
        }

        public async Task StartAsync()
        {
            if (!_settings.IsSignalREnabled || string.IsNullOrWhiteSpace(_settings.SignalRHubUrl))
                return;

            string sanitizedUrl = _settings.SignalRHubUrl.Replace("//radiohub", "/radiohub");
            if (sanitizedUrl.EndsWith("/")) sanitizedUrl = sanitizedUrl.TrimEnd('/');

            if (_connection != null)
                await StopAsync();

            // ADVERTENCIA: En DEBUG se acepta cualquier certificado SSL para desarrollo local.
            // En producción (Release), se validan certificados normalmente.
            var builder = new HubConnectionBuilder()
                .WithUrl(sanitizedUrl
#if DEBUG
                    , options => {
                        options.HttpMessageHandlerFactory = (handler) =>
                        {
                            if (handler is System.Net.Http.HttpClientHandler clientHandler)
                            {
                                clientHandler.ServerCertificateCustomValidationCallback =
                                    System.Net.Http.HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                            }
                            return handler;
                        };
                    }
#endif
                )
                .WithAutomaticReconnect();

            _connection = builder.Build();

            // Listen for remote commands from Dashboard
            _connection.On<RadioLogger.Shared.Models.StationCommand>("ReceiveCommand", (command) =>
            {
                System.Diagnostics.Debug.WriteLine($"[SignalR] Command received: {command.Command} for {command.HardwareName}");
                CommandReceived?.Invoke(command);
            });

            _connection.Closed += async (error) =>
            {
                if (_isDisposed) return;
                System.Diagnostics.Debug.WriteLine($"SignalR Connection Closed: {error?.Message}");
                await Task.Delay(new Random().Next(0, 5) * 1000);
                if (!_isDisposed)
                    await StartAsync();
            };

            // PERSISTENT RETRY LOOP
            bool connected = false;
            int attempts = 0;
            
            while (!connected && !_isDisposed)
            {
                try
                {
                    attempts++;
                    System.Diagnostics.Debug.WriteLine($"SignalR Connection Attempt {attempts}...");
                    await _connection.StartAsync();
                    System.Diagnostics.Debug.WriteLine("SignalR Connected Successfully.");
                    ConnectionStatusChanged?.Invoke("Monitoreo Web Conectado", true);
                    connected = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SignalR Connection Error (Attempt {attempts}): {ex.Message}");
                    ConnectionStatusChanged?.Invoke($"Esperando servidor ({attempts})...", false);
                    
                    // Wait 5 seconds before trying again
                    await Task.Delay(5000);
                }
            }
        }

        public async Task StopAsync()
        {
            if (_connection != null)
            {
                await _connection.StopAsync();
                await _connection.DisposeAsync();
                _connection = null;
            }
        }

        /// <summary>
        /// Individual station update using shared model
        /// </summary>
        public async Task SendStatusUpdateAsync(StationStatusUpdate update)
        {
            if (!IsConnected) return;

            try
            {
                await _connection!.InvokeAsync("UpdateStationStatus", update);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SignalR Send Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Batch update for performance using shared model
        /// </summary>
        public async Task SendBatchUpdateAsync(BatchStatusUpdate batch)
        {
            if (!IsConnected) return;

            try
            {
                await _connection!.InvokeAsync("UpdateBatchStatus", batch);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SignalR Batch Send Error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                try
                {
                    _connection?.StopAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SignalR Stop error on dispose: {ex.Message}");
                }
                try
                {
                    _connection?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SignalR Dispose error: {ex.Message}");
                }
                _connection = null;
            }
        }
    }
}
