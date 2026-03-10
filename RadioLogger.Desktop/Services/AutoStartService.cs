using Microsoft.Win32;
using System;
using System.Diagnostics;

namespace RadioLogger.Services
{
    public static class AutoStartService
    {
        private const string AppName = "RadioLogger2026";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static void SetAutoStart(bool enable)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return;

                    if (enable)
                    {
                        string? path = Process.GetCurrentProcess().MainModule?.FileName;
                        if (!string.IsNullOrEmpty(path))
                        {
                            // Add argument to start minimized
                            key.SetValue(AppName, $"\"{path}\" --minimized");
                        }
                    }
                    else
                    {
                        key.DeleteValue(AppName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting auto-start: {ex.Message}");
            }
        }
    }
}
