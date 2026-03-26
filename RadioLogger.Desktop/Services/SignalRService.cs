using Microsoft.AspNetCore.SignalR.Client;
using RadioLogger.Models;
using RadioLogger.Shared.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RadioLogger.Services
{
    public class SignalRService : IDisposable
    {
        private static readonly ILogger _log = AppLog.For<SignalRService>();

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

            _connection.On<RadioLogger.Shared.Models.StationCommand>("ReceiveCommand", (command) =>
            {
                _log.Information("Comando recibido vía SignalR: {Command} para {HardwareName}", command.Command, command.HardwareName);
                CommandReceived?.Invoke(command);
            });

            // Dashboard solicita archivo de log de una fecha específica
            _connection.On<LogFileRequest>("RequestLogFile", async (request) =>
            {
                _log.Debug("Solicitud de log remoto: {Date}", request.Date);
                var response = ReadLocalLogFile(request);
                if (_connection != null)
                    await _connection.InvokeAsync("SendLogFileResponse", response);
            });

            _connection.Reconnecting += (error) =>
            {
                _log.Warning("SignalR reconectando: {Error}", error?.Message ?? "conexión perdida");
                ConnectionStatusChanged?.Invoke("Monitoreo: Reconectando...", false);
                return Task.CompletedTask;
            };

            _connection.Reconnected += (connectionId) =>
            {
                _log.Information("SignalR reconectado exitosamente (ID: {ConnectionId})", connectionId);
                ConnectionStatusChanged?.Invoke("Monitoreo Web Conectado", true);
                return Task.CompletedTask;
            };

            _connection.Closed += async (error) =>
            {
                if (_isDisposed) return;
                _log.Warning("SignalR conexión cerrada: {Error}", error?.Message ?? "sin error");
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
                    _log.Debug("SignalR intento de conexión #{Attempt} a {Url}", attempts, sanitizedUrl);
                    await _connection.StartAsync();
                    _log.Information("SignalR conectado a {Url}", sanitizedUrl);
                    ConnectionStatusChanged?.Invoke("Monitoreo Web Conectado", true);
                    connected = true;
                }
                catch (Exception ex)
                {
                    _log.Warning("SignalR conexión fallida (intento {Attempt}): {Error}", attempts, ex.Message);
                    ConnectionStatusChanged?.Invoke($"Esperando servidor ({attempts})...", false);
                    await Task.Delay(5000);
                }
            }
        }

        public async Task StopAsync()
        {
            if (_connection != null)
            {
                _log.Debug("SignalR deteniendo conexión");
                await _connection.StopAsync();
                await _connection.DisposeAsync();
                _connection = null;
            }
        }

        public async Task SendStatusUpdateAsync(StationStatusUpdate update)
        {
            if (!IsConnected) return;

            try
            {
                await _connection!.InvokeAsync("UpdateStationStatus", update);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Error enviando actualización SignalR");
            }
        }

        public async Task SendBatchUpdateAsync(BatchStatusUpdate batch)
        {
            if (!IsConnected) return;

            try
            {
                await _connection!.InvokeAsync("UpdateBatchStatus", batch);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Error enviando batch SignalR");
            }
        }

        public async Task SendMachineInfoAsync(MachineInfo info)
        {
            if (!IsConnected) return;
            try
            {
                await _connection!.InvokeAsync("RegisterMachineInfo", info);
                _log.Information("MachineInfo enviado: v{Version}, AutoLogin={AL}, AutoStart={AS}", info.AppVersion, info.AutoLoginEnabled, info.AutoStartEnabled);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Error enviando MachineInfo");
            }
        }

        public async Task SendLogEntriesAsync(LogEntryBatch batch)
        {
            if (!IsConnected) return;

            try
            {
                await _connection!.InvokeAsync("SendLogEntries", batch);
            }
            catch
            {
                // Silenciar — no loguear aquí para evitar recursión infinita
            }
        }

        private static LogFileResponse ReadLocalLogFile(LogFileRequest request)
        {
            var response = new LogFileResponse
            {
                MachineId = Environment.MachineName,
                Date = request.Date
            };

            try
            {
                var logDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                var fileName = $"radiologger-{request.Date}.log";
                var filePath = System.IO.Path.Combine(logDir, fileName);

                if (System.IO.File.Exists(filePath))
                {
                    using var fs = new System.IO.FileStream(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
                    using var reader = new System.IO.StreamReader(fs);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                            response.Lines.Add(line);
                    }
                }
            }
            catch { }

            return response;
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                _log.Debug("SignalR disposing");
                try
                {
                    _connection?.StopAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Error deteniendo SignalR en dispose");
                }
                try
                {
                    _connection?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Error disposing SignalR");
                }
                _connection = null;
            }
        }
    }
}
