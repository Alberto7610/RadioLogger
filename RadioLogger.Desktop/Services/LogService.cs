using System;
using System.IO;

namespace RadioLogger.Services
{
    public enum LogCategory { SYSTEM, AUDIO, NETWORK }

    public static class LogService
    {
        private static string _basePath = string.Empty;
        private static readonly object _lock = new object();

        public static void Initialize(string basePath)
        {
            _basePath = Path.Combine(basePath, "RadioLogger", "Logs");
            if (!Directory.Exists(_basePath)) Directory.CreateDirectory(_basePath);
            Log(LogCategory.SYSTEM, "Servicio de Log Iniciado.");
        }

        public static void Log(LogCategory category, string message)
        {
            if (string.IsNullOrEmpty(_basePath)) return;

            lock (_lock)
            {
                try
                {
                    string fileName = $"Log-{DateTime.Now:ddMMyy}.txt";
                    string filePath = Path.Combine(_basePath, fileName);
                    string categoryTag = category switch
                    {
                        LogCategory.SYSTEM => "[SISTEMA  ]",
                        LogCategory.AUDIO => "[AUDIO/GRB]",
                        LogCategory.NETWORK => "[STREAM/RD]",
                        _ => "[INFO     ]"
                    };

                    string entry = $"{DateTime.Now:HH:mm:ss} {categoryTag} {message}";
                    File.AppendAllLines(filePath, new[] { entry });
                }
                catch { /* Fail silently to not crash the app */ }
            }
        }
    }
}