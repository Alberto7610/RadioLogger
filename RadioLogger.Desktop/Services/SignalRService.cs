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

        public bool IsConnected => _connection?.State == HubConnectionState.Connected;

        public SignalRService(AppSettings settings)
        {
            _settings = settings;
        }

        public async Task StartAsync()
        {
            if (!_settings.IsSignalREnabled || string.IsNullOrWhiteSpace(_settings.SignalRHubUrl))
                return;

            if (_connection != null)
                await StopAsync();

            _connection = new HubConnectionBuilder()
                .WithUrl(_settings.SignalRHubUrl.Replace("5046", "5000"), options => {
                    options.HttpMessageHandlerFactory = (handler) =>
                    {
                        if (handler is System.Net.Http.HttpClientHandler clientHandler)
                        {
                            clientHandler.ServerCertificateCustomValidationCallback = 
                                (System.Net.Http.HttpRequestMessage m, 
                                 System.Security.Cryptography.X509Certificates.X509Certificate2? c, 
                                 System.Security.Cryptography.X509Certificates.X509Chain? ch, 
                                 System.Net.Security.SslPolicyErrors e) => true;
                        }
                        return handler;
                    };
                })
                .WithAutomaticReconnect()
                .Build();

            _connection.Closed += async (error) =>
            {
                System.Diagnostics.Debug.WriteLine($"SignalR Connection Closed: {error?.Message}");
                await Task.Delay(new Random().Next(0, 5) * 1000);
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
                _connection?.DisposeAsync().AsTask().Wait();
                _isDisposed = true;
            }
        }
    }
}
