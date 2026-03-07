using System.Windows;
using System.Windows.Input;

namespace RadioLogger.Views
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            
            // Allow dragging the window by clicking anywhere on the background
            this.MouseLeftButtonDown += (s, e) => 
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                    this.DragMove();
            };
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            // The Cancel button uses IsCancel="True" which automatically sets DialogResult = false.
            // This handler is only for the Save button (or any button intended to return Success).
            this.DialogResult = true;
            this.Close();
        }
    }
}