using System;
using System.IO;

namespace RadioLogger.Services
{
    public static class DebugLog
    {
        private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log");
        private static readonly object _lock = new();

        public static void Write(string message)
        {
            try
            {
                string line = $"{DateTime.Now:HH:mm:ss.fff} [DEBUG] {message}{Environment.NewLine}";
                lock (_lock)
                {
                    File.AppendAllText(LogPath, line);
                }
            }
            catch { }
        }
    }
}
