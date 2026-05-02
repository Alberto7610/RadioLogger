using RadioLogger.Shared.Models;
using RadioLogger.Web.Data;

namespace RadioLogger.Web.Services
{
    public class DashboardLogService
    {
        private readonly IServiceProvider _serviceProvider;
        private const string MachineId = "DASHBOARD";

        public DashboardLogService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void LogInfo(string source, string message)
            => _ = WriteAsync("INF", source, message);

        public void LogWarning(string source, string message)
            => _ = WriteAsync("WRN", source, message);

        public void LogError(string source, string message, Exception? ex = null)
            => _ = WriteAsync("ERR", source, message, ex?.ToString());

        private async Task WriteAsync(string level, string source, string message, string? exception = null)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
                db.LogEntries.Add(new LogEntry
                {
                    MachineId = MachineId,
                    Timestamp = DateTime.UtcNow,
                    Level = level,
                    Source = source,
                    Message = message,
                    Exception = exception
                });
                await db.SaveChangesAsync();
            }
            catch
            {
                // Último recurso — no puede fallar el logging en sí
            }
        }
    }
}
