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
    }
}