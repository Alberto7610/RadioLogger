using System;
using System.IO;

namespace RadioLogger.Services
{
    public static class DebugLog
    {
        private static string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log");

        public static void Write(string message)
        {
            try
            {
                string line = $"{DateTime.Now:HH:mm:ss.fff} [DEBUG] {message}{Environment.NewLine}";
                File.AppendAllText(LogPath, line);
            }
            catch { }
        }
    }
}
