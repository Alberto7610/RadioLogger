using System.Windows;
using System.Windows.Input;

namespace RadioLogger.Views
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();

            // Allow dragging the window by clicking on non-interactive areas
            this.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not System.Windows.Controls.TextBox
                    && e.OriginalSource is not System.Windows.Controls.PasswordBox
                    && e.OriginalSource is not System.Windows.Controls.Button
                    && e.OriginalSource is not System.Windows.Controls.CheckBox
                    && e.OriginalSource is not System.Windows.Controls.ComboBox
                    && e.OriginalSource is not System.Windows.Controls.Slider)
                    this.DragMove();
            };

            Closed += (s, e) =>
            {
                if (DataContext is RadioLogger.ViewModels.SettingsViewModel vm)
                    vm.StopLogAutoRefresh();
            };
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }

        // PasswordBox doesn't support binding — bridge via code-behind
        private void PwdCurrent_Changed(object sender, RoutedEventArgs e)
        {
            if (DataContext is RadioLogger.ViewModels.SettingsViewModel vm)
                vm.CurrentPassword = PwdCurrent.Password;
        }

        private void PwdNew_Changed(object sender, RoutedEventArgs e)
        {
            if (DataContext is RadioLogger.ViewModels.SettingsViewModel vm)
                vm.NewPassword = PwdNew.Password;
        }

        private void PwdConfirm_Changed(object sender, RoutedEventArgs e)
        {
            if (DataContext is RadioLogger.ViewModels.SettingsViewModel vm)
                vm.ConfirmPassword = PwdConfirm.Password;
        }

        private void AutoLoginPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is RadioLogger.ViewModels.SettingsViewModel vm)
                vm.AutoLoginPassword = AutoLoginPasswordBox.Password;
        }

        private void CopyLogs_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is RadioLogger.ViewModels.SettingsViewModel vm)
                vm.CopySelectedLogsCommand.Execute(LogListBox.SelectedItems);
        }

        private bool _isPasswordVisible;
        private void TogglePasswordVisibility_Click(object sender, RoutedEventArgs e)
        {
            _isPasswordVisible = !_isPasswordVisible;
            if (_isPasswordVisible)
            {
                AutoLoginPasswordVisible.Text = AutoLoginPasswordBox.Password;
                AutoLoginPasswordBox.Visibility = Visibility.Collapsed;
                AutoLoginPasswordVisible.Visibility = Visibility.Visible;
                AutoLoginPasswordVisible.Focus();
            }
            else
            {
                AutoLoginPasswordBox.Password = AutoLoginPasswordVisible.Text;
                AutoLoginPasswordVisible.Visibility = Visibility.Collapsed;
                AutoLoginPasswordBox.Visibility = Visibility.Visible;
                AutoLoginPasswordBox.Focus();
            }
        }
    }
}