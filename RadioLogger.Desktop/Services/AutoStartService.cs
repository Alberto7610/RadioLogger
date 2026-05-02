using Microsoft.Win32;
using System;
using System.Diagnostics;

namespace RadioLogger.Services
{
    public static class AutoStartService
    {
        private const string AppName = "RadioLogger2026";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string WinlogonKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";

        /// <summary>
        /// Enables/disables auto-start via HKCU\Run registry.
        /// No UAC required — HKCU is user-level.
        /// </summary>
        public static void SetAutoStart(bool enable)
        {
            try
            {
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return;

                using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
                if (key == null) return;

                if (enable)
                    key.SetValue(AppName, $"\"{exePath}\" --minimized");
                else
                    key.DeleteValue(AppName, false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting auto-start: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if auto-start is enabled in HKCU\Run.
        /// </summary>
        public static bool IsAutoStartEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
                return key?.GetValue(AppName) != null;
            }
            catch { return false; }
        }


        /// <summary>
        /// Checks if Windows auto-login is currently enabled in the registry.
        /// </summary>
        public static bool IsAutoLoginEnabled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(WinlogonKey, false);
                if (key == null) return false;
                var val = key.GetValue("AutoAdminLogon") as string;
                return val == "1";
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Enables/disables Windows auto-login by launching an elevated copy of the app.
        /// The UAC prompt will appear (yellow dialog) and the elevated process writes to HKLM.
        /// </summary>
        public static (bool success, string message) SetAutoLogin(bool enable, string? username = null, string? password = null)
        {
            try
            {
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                    return (false, "No se pudo determinar la ruta del ejecutable.");

                string arguments;
                if (enable)
                {
                    if (string.IsNullOrEmpty(username) || password == null)
                        return (false, "Se requiere usuario y contraseña.");

                    arguments = $"--set-autologin \"{username}\" \"{password}\"";
                }
                else
                {
                    arguments = "--remove-autologin";
                }

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments,
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                var proc = Process.Start(psi);
                if (proc == null)
                    return (false, "No se pudo iniciar el proceso elevado.");

                proc.WaitForExit(5000);

                if (proc.ExitCode == 0)
                {
                    return enable
                        ? (true, "Auto-login habilitado. El equipo iniciará sesión automáticamente al reiniciar.")
                        : (true, "Auto-login deshabilitado.");
                }

                return (false, "El proceso elevado terminó con error.");
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // El usuario canceló el UAC
                return (false, "Operación cancelada por el usuario.");
            }
            catch (Exception ex)
            {
                return (false, $"Error: {ex.Message}");
            }
        }
    }
}
