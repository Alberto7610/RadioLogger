using Microsoft.Win32;
using System;
using System.Linq;
using System.Windows;

namespace RadioLogger;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private const string WinlogonKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
    private const string PasswordLessKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\PasswordLess\Device";

    protected override void OnStartup(StartupEventArgs e)
    {
        var args = e.Args;

        if (args.Length >= 1 && args[0] == "--set-autologin" && args.Length >= 3)
        {
            string username = args[1];
            string password = args[2];
            try
            {
                // Windows 11 24H2/25H2: Desactivar "Passwordless sign-in" que bloquea AutoAdminLogon
                using var pwdLessKey = Registry.LocalMachine.OpenSubKey(PasswordLessKey, true);
                pwdLessKey?.SetValue("DevicePasswordLessBuildVersion", 0, RegistryValueKind.DWord);

                using var key = Registry.LocalMachine.OpenSubKey(WinlogonKey, true);
                if (key != null)
                {
                    key.SetValue("AutoAdminLogon", "1");
                    key.SetValue("DefaultUserName", username);
                    key.SetValue("DefaultPassword", password);
                }
            }
            catch { }
            Shutdown(0);
            return;
        }

        if (args.Length >= 1 && args[0] == "--remove-autologin")
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(WinlogonKey, true);
                if (key != null)
                {
                    key.SetValue("AutoAdminLogon", "0");
                    key.DeleteValue("DefaultPassword", false);
                }
            }
            catch { }
            Shutdown(0);
            return;
        }

        // Verificar si debe iniciar minimizado (auto-start desde registro de Windows)
        bool startMinimized = args.Any(a => a == "--minimized");

        base.OnStartup(e);

        if (startMinimized && MainWindow != null)
        {
            // Ocultar la ventana para que solo quede en el tray
            MainWindow.Hide();
        }
    }
}

