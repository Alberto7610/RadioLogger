using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace RadioLogger.Views
{
    public partial class PasswordDialog : Window
    {
        private readonly string _expectedHash;
        private int _attempts = 0;
        private const int MaxAttempts = 5;

        public bool IsAuthenticated { get; private set; }

        public PasswordDialog(string expectedHash)
        {
            InitializeComponent();
            _expectedHash = expectedHash;
            Loaded += (_, _) => PasswordInput.Focus();
        }

        private void Accept_Click(object sender, RoutedEventArgs e) => ValidatePassword();

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            IsAuthenticated = false;
            DialogResult = false;
        }

        private void PasswordInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter) ValidatePassword();
            else if (e.Key == Key.Escape) { IsAuthenticated = false; DialogResult = false; }
        }

        private void ValidatePassword()
        {
            string input = PasswordInput.Password;
            string hash = ComputeHash(input);

            if (hash == _expectedHash)
            {
                IsAuthenticated = true;
                DialogResult = true;
            }
            else
            {
                _attempts++;
                if (_attempts >= MaxAttempts)
                {
                    ErrorText.Text = "Demasiados intentos. Cierre e intente de nuevo.";
                    ErrorText.Visibility = Visibility.Visible;
                    PasswordInput.IsEnabled = false;
                }
                else
                {
                    ErrorText.Text = $"Contraseña incorrecta. Intento {_attempts}/{MaxAttempts}";
                    ErrorText.Visibility = Visibility.Visible;
                    PasswordInput.Clear();
                    PasswordInput.Focus();
                }
            }
        }

        public static string ComputeHash(string input)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexStringLower(bytes);
        }
    }
}
