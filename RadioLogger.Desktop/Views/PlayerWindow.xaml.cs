using System;
using System.Windows;

namespace RadioLogger.Views
{
    public partial class PlayerWindow : Window
    {
        public PlayerWindow()
        {
            InitializeComponent();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            
            // Stop playback and free resources when window closes
            if (DataContext is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
