using Microsoft.AspNetCore.SignalR.Client;
using RadioLogger.Models;
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
                .WithUrl(_settings.SignalRHubUrl)
                .WithAutomaticReconnect()
                .Build();

            try
            {
                await _connection.StartAsync();
                System.Diagnostics.Debug.WriteLine("SignalR Connected.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SignalR Connection Error: {ex.Message}");
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

        public async Task SendStatusUpdateAsync(string stationName, double leftLevel, double rightLevel, bool isRecording, bool isStreaming, bool isSilence)
        {
            if (!IsConnected) return;

            try
            {
                await _connection!.InvokeAsync("UpdateStationStatus", new
                {
                    StationName = stationName,
                    LeftLevel = leftLevel,
                    RightLevel = rightLevel,
                    IsRecording = isRecording,
                    IsStreaming = isStreaming,
                    IsSilence = isSilence,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SignalR Send Error: {ex.Message}");
            }
        }

        public async Task SendBatchUpdateAsync(IEnumerable<object> updates)
        {
            if (!IsConnected) return;

            try
            {
                await _connection!.InvokeAsync("UpdateBatchStatus", updates);
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
