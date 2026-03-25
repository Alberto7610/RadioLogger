using Microsoft.Extensions.Configuration;
using Microsoft.Win32;
using Serilog;
using System;
using System.IO;
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
        // Configurar Serilog lo antes posible
        InitializeLogging();

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
            catch (Exception ex)
            {
                Log.Error(ex, "Error configurando auto-login de Windows");
            }
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
            catch (Exception ex)
            {
                Log.Error(ex, "Error removiendo auto-login de Windows");
            }
            Shutdown(0);
            return;
        }

        // Red de seguridad: capturar excepciones no manejadas
        AppDomain.CurrentDomain.UnhandledException += (s, ue) =>
            Log.Fatal(ue.ExceptionObject as Exception, "Excepción no manejada (AppDomain)");
        DispatcherUnhandledException += (s, de) =>
        {
            Log.Fatal(de.Exception, "Excepción no manejada (Dispatcher)");
            de.Handled = false; // Dejar que la app decida si puede continuar
        };
        TaskScheduler.UnobservedTaskException += (s, te) =>
        {
            Log.Error(te.Exception, "Excepción no observada en Task");
            te.SetObserved();
        };

        // Verificar si debe iniciar minimizado (auto-start desde registro de Windows)
        bool startMinimized = args.Any(a => a == "--minimized");

        Log.Information("RadioLogger iniciado en {MachineName} (minimizado: {Minimized})", Environment.MachineName, startMinimized);

        base.OnStartup(e);

        if (startMinimized && MainWindow != null)
        {
            // Ocultar la ventana para que solo quede en el tray
            MainWindow.Hide();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("RadioLogger cerrando");
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static void InitializeLogging()
    {
        var basePath = AppDomain.CurrentDomain.BaseDirectory;

        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("logsettings.json", optional: true, reloadOnChange: false)
            .Build();

        var logPath = Path.Combine(basePath, "logs", "radiologger-.log");

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            // Override path to use absolute path based on exe location
            .WriteTo.File(
                logPath,
                rollingInterval: Serilog.RollingInterval.Day,
                retainedFileCountLimit: 30,
                fileSizeLimitBytes: 52_428_800, // 50 MB
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                shared: true)
#if DEBUG
            .MinimumLevel.Debug()
            .WriteTo.Debug()
#endif
            .CreateLogger();
    }
}
