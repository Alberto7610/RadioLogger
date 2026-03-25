using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace RadioLogger;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private RadioLogger.Services.TrayService? _trayService;

    public MainWindow()
    {
        InitializeComponent();
        
        // Initialize Tray Icon logic
        _trayService = new RadioLogger.Services.TrayService(this);

        // Subscribirse al evento de cierre para ir al tray
        this.Closing += MainWindow_Closing;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Si no estamos cerrando desde el icono del Tray (botón Salir), cancelamos el cierre y ocultamos
        if (_trayService != null && !_trayService.IsClosingFromTray)
        {
            e.Cancel = true;
            this.Hide();
        }
        else
        {
            // Persistir estado y limpiar recursos antes de cerrar
            if (DataContext is RadioLogger.ViewModels.MainViewModel vm)
            {
                vm.PersistCurrentState();
                vm.Cleanup();
            }
            _trayService?.Dispose();
            _trayService = null;
        }
    }
}