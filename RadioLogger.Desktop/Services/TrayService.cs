using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace RadioLogger.Services
{
    public class TrayService : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly Window _mainWindow;
        private bool _isClosingFromTray = false;

        public bool IsClosingFromTray => _isClosingFromTray;

        public TrayService(Window mainWindow)
        {
            _mainWindow = mainWindow;
            
            _notifyIcon = new NotifyIcon();
            
            try
            {
                // Intentar cargar el icono directamente del archivo para máxima compatibilidad
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ico", "CRL.ico");
                if (File.Exists(iconPath))
                {
                    _notifyIcon.Icon = new Icon(iconPath);
                }
                else
                {
                    // Fallback al icono del ensamblado
                    _notifyIcon.Icon = Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
                }
            }
            catch
            {
                _notifyIcon.Icon = SystemIcons.Application;
            }

            _notifyIcon.Text = "Cloud Radio Logger 2026";
            _notifyIcon.Visible = true;
            
            // Context Menu
            var contextMenu = new ContextMenuStrip();
            var restoreItem = new ToolStripMenuItem("Restaurar Consola");
            restoreItem.Click += (s, e) => RestoreWindow();
            contextMenu.Items.Add(restoreItem);
            
            contextMenu.Items.Add(new ToolStripSeparator());
            
            var exitItem = new ToolStripMenuItem("Cerrar Cloud Radio Logger");
            exitItem.Click += (s, e) => ShutdownApp();
            contextMenu.Items.Add(exitItem);
            
            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.DoubleClick += (s, e) => RestoreWindow();

            _mainWindow.StateChanged += OnWindowStateChanged;
        }

        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            if (_mainWindow.WindowState == WindowState.Minimized)
            {
                _mainWindow.Hide();
            }
        }

        public void RestoreWindow()
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }

        private void ShutdownApp()
        {
            _isClosingFromTray = true;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            
            // Forzar el cierre real de la ventana antes de apagar
            Application.Current.Shutdown();
        }

        public void Dispose()
        {
            _mainWindow.StateChanged -= OnWindowStateChanged;
            _notifyIcon.Dispose();
        }
    }
}