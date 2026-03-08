using System;
using System.Drawing;
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

        public TrayService(Window mainWindow)
        {
            _mainWindow = mainWindow;
            
            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
            _notifyIcon.Text = "RadioLogger 2026 - Grabando...";
            _notifyIcon.Visible = true;
            
            // Context Menu
            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Restaurar Consola", null, (s, e) => RestoreWindow());
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("Cerrar RadioLogger", null, (s, e) => ShutdownApp());
            
            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.DoubleClick += (s, e) => RestoreWindow();

            // Intercept Window Closing
            _mainWindow.Closing += OnWindowClosing;
            _mainWindow.StateChanged += OnWindowStateChanged;
        }

        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            if (_mainWindow.WindowState == WindowState.Minimized)
            {
                _mainWindow.Hide();
                _notifyIcon.ShowBalloonTip(2000, "RadioLogger", "La grabación continúa en segundo plano.", ToolTipIcon.Info);
            }
        }

        private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isClosingFromTray)
            {
                e.Cancel = true;
                _mainWindow.Hide();
                _notifyIcon.ShowBalloonTip(2000, "RadioLogger", "Minimizado al tray.", ToolTipIcon.Info);
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
            _mainWindow.Close();
            Application.Current.Shutdown();
        }

        public void Dispose()
        {
            _notifyIcon.Dispose();
        }
    }
}
