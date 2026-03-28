using RadioLogger.Shared.Models;
using Serilog.Core;
using Serilog.Events;
using System;

namespace RadioLogger.Services
{
    /// <summary>
    /// Serilog Sink that sends log entries (Info+) to the Dashboard via SignalR.
    /// Se inicializa vacío y se activa cuando se asigna el SignalRService.
    /// </summary>
    public class SignalRLogSink : ILogEventSink
    {
        public static SignalRLogSink Instance { get; } = new();

        private SignalRService? _signalR;
        private readonly string _machineId = Environment.MachineName;

        /// <summary>
        /// Activar el sink conectándolo al servicio SignalR.
        /// </summary>
        public void Activate(SignalRService signalR)
        {
            _signalR = signalR;
        }

        public void Emit(LogEvent logEvent)
        {
            var signalR = _signalR; // Referencia local para evitar race condition
            if (signalR == null || !signalR.IsConnected) return;
            if (logEvent.Level < LogEventLevel.Information) return;

            var entry = new LogEntry
            {
                MachineId = _machineId,
                Timestamp = logEvent.Timestamp.UtcDateTime,
                Level = LevelToTag(logEvent.Level),
                Source = ExtractSourceContext(logEvent),
                Message = logEvent.RenderMessage(),
                Exception = logEvent.Exception?.ToString()
            };

            var batch = new LogEntryBatch
            {
                MachineId = _machineId,
                Entries = [entry]
            };

            _ = signalR.SendLogEntriesAsync(batch);
        }

        private static string LevelToTag(LogEventLevel level) => level switch
        {
            LogEventLevel.Debug => "DBG",
            LogEventLevel.Information => "INF",
            LogEventLevel.Warning => "WRN",
            LogEventLevel.Error => "ERR",
            LogEventLevel.Fatal => "FTL",
            _ => "INF"
        };

        private static string ExtractSourceContext(LogEvent logEvent)
        {
            if (logEvent.Properties.TryGetValue("SourceContext", out var value))
            {
                var ctx = value.ToString().Trim('"');
                var lastDot = ctx.LastIndexOf('.');
                return lastDot >= 0 ? ctx[(lastDot + 1)..] : ctx;
            }
            return "";
        }
    }
}
