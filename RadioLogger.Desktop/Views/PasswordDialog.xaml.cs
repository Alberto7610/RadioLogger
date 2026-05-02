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

        /// <summary>
        /// If the stored hash was legacy SHA256 and the user authenticated successfully,
        /// this contains the new BCrypt hash to persist. Null if no migration needed.
        /// </summary>
        public string? MigratedHash { get; private set; }

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

            bool valid;
            if (IsLegacySha256(_expectedHash))
            {
                // Legacy SHA256: compare hashes, then migrate to BCrypt
                string sha256Hash = ComputeSha256(input);
                valid = sha256Hash == _expectedHash;

                if (valid)
                {
                    // Auto-migrate to BCrypt
                    MigratedHash = HashPassword(input);
                }
            }
            else
            {
                // BCrypt verification
                valid = VerifyPassword(input, _expectedHash);
            }

            if (valid)
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

        /// <summary>
        /// Detects if a hash is legacy SHA256 (64 hex chars) vs BCrypt ($2a$/$2b$ prefix).
        /// </summary>
        private static bool IsLegacySha256(string hash)
        {
            return hash.Length == 64 && !hash.StartsWith("$2");
        }

        /// <summary>
        /// Hashes a password with BCrypt (work factor 12).
        /// </summary>
        public static string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
        }

        /// <summary>
        /// Verifies a password against a BCrypt hash.
        /// </summary>
        public static bool VerifyPassword(string password, string hash)
        {
            try
            {
                return BCrypt.Net.BCrypt.Verify(password, hash);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Legacy SHA256 hash (kept for migration only).
        /// </summary>
        public static string ComputeHash(string input)
        {
            return HashPassword(input);
        }

        private static string ComputeSha256(string input)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexStringLower(bytes);
        }
    }
}
