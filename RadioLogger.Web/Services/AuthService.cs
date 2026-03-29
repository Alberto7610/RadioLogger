using Microsoft.EntityFrameworkCore;
using RadioLogger.Shared.Models;
using RadioLogger.Web.Data;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace RadioLogger.Web.Services
{
    public class AuthService
    {
        private readonly IServiceProvider _serviceProvider;

        // Rate limiting: username → (failCount, lockUntil)
        private static readonly ConcurrentDictionary<string, (int fails, DateTime lockUntil)> _loginAttempts = new();
        private const int MaxAttempts = 5;
        private const int LockoutMinutes = 15;

        public AuthService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task<(AppUser? user, string? error)> ValidateLoginAsync(string username, string password)
        {
            // Rate limiting check
            if (_loginAttempts.TryGetValue(username, out var attempt))
            {
                if (attempt.lockUntil > DateTime.UtcNow)
                {
                    var minutesLeft = (int)(attempt.lockUntil - DateTime.UtcNow).TotalMinutes + 1;
                    return (null, $"Cuenta bloqueada. Intenta en {minutesLeft} minutos.");
                }
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();

            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username && u.IsActive);

            if (user != null && BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            {
                // Login exitoso — limpiar intentos
                _loginAttempts.TryRemove(username, out _);
                user.LastLogin = DateTime.UtcNow;
                await db.SaveChangesAsync();
                return (user, null);
            }

            // Login fallido — incrementar contador
            var current = _loginAttempts.GetOrAdd(username, (0, DateTime.MinValue));
            var newFails = current.fails + 1;
            var lockUntil = newFails >= MaxAttempts
                ? DateTime.UtcNow.AddMinutes(LockoutMinutes)
                : DateTime.MinValue;
            _loginAttempts[username] = (newFails, lockUntil);

            if (newFails >= MaxAttempts)
                return (null, $"Demasiados intentos. Cuenta bloqueada por {LockoutMinutes} minutos.");

            return (null, $"Usuario o contraseña incorrectos ({MaxAttempts - newFails} intentos restantes)");
        }

        public async Task<List<AppUser>> GetAllUsersAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
            return await db.Users.OrderBy(u => u.Username).ToListAsync();
        }

        public async Task<AppUser?> GetUserAsync(int id)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
            return await db.Users.FindAsync(id);
        }

        public async Task<(bool success, string message)> CreateUserAsync(string username, string password, string displayName, string role, string? telegramChatId)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();

            if (await db.Users.AnyAsync(u => u.Username == username))
                return (false, "El usuario ya existe");

            if (password.Length < 6)
                return (false, "La contraseña debe tener al menos 6 caracteres");

            var user = new AppUser
            {
                Username = username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                DisplayName = displayName,
                Role = role,
                TelegramChatId = telegramChatId,
                IsActive = true
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();
            return (true, "Usuario creado");
        }

        public async Task<(bool success, string message)> UpdateUserAsync(int id, string displayName, string role, bool isActive, string? telegramChatId)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();

            var user = await db.Users.FindAsync(id);
            if (user == null) return (false, "Usuario no encontrado");

            user.DisplayName = displayName;
            user.Role = role;
            user.IsActive = isActive;
            user.TelegramChatId = telegramChatId;
            await db.SaveChangesAsync();
            return (true, "Usuario actualizado");
        }

        public async Task<(bool success, string message)> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
        {
            if (newPassword.Length < 6)
                return (false, "La contraseña debe tener al menos 6 caracteres");

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();

            var user = await db.Users.FindAsync(userId);
            if (user == null) return (false, "Usuario no encontrado");

            // Verificar contraseña actual (excepto si es admin reseteando)
            if (!string.IsNullOrEmpty(currentPassword) && !BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
                return (false, "Contraseña actual incorrecta");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            await db.SaveChangesAsync();
            return (true, "Contraseña actualizada");
        }

        /// <summary>
        /// Admin reset — no requiere password actual.
        /// </summary>
        public async Task<(bool success, string message)> AdminResetPasswordAsync(int userId, string newPassword)
        {
            if (newPassword.Length < 6)
                return (false, "La contraseña debe tener al menos 6 caracteres");

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();

            var user = await db.Users.FindAsync(userId);
            if (user == null) return (false, "Usuario no encontrado");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            await db.SaveChangesAsync();
            return (true, "Contraseña actualizada");
        }

        public async Task<(bool success, string message)> ResetPasswordWithCodeAsync(string username, string code, string newPassword)
        {
            // Rate limiting en reset
            var resetKey = $"reset:{username}";
            if (_loginAttempts.TryGetValue(resetKey, out var attempt) && attempt.lockUntil > DateTime.UtcNow)
                return (false, "Demasiados intentos. Espera unos minutos.");

            var expectedCode = GetStoredRecoveryCode(username);
            if (expectedCode == null || !string.Equals(code.Trim(), expectedCode, StringComparison.OrdinalIgnoreCase))
            {
                // Track failed reset attempts
                var current = _loginAttempts.GetOrAdd(resetKey, (0, DateTime.MinValue));
                _loginAttempts[resetKey] = (current.fails + 1,
                    current.fails + 1 >= 5 ? DateTime.UtcNow.AddMinutes(15) : DateTime.MinValue);
                return (false, "Código inválido o expirado");
            }

            if (newPassword.Length < 6)
                return (false, "La contraseña debe tener al menos 6 caracteres");

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();

            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username && u.IsActive);
            if (user == null) return (false, "Error al restablecer");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            await db.SaveChangesAsync();

            // Limpiar código usado
            _recoveryCodes.TryRemove(username, out _);
            _loginAttempts.TryRemove(resetKey, out _);

            return (true, "Contraseña restablecida");
        }

        // Códigos de recuperación aleatorios (no predecibles)
        private static readonly ConcurrentDictionary<string, (string code, DateTime expires)> _recoveryCodes = new();

        public async Task<(bool success, string message)> SendRecoveryCodeAsync(string username)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();

            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username && u.IsActive);

            // Mensaje genérico siempre (no revelar si existe)
            if (user == null || string.IsNullOrEmpty(user.TelegramChatId))
                return (true, "Si el usuario existe y tiene Telegram configurado, recibirá el código.");

            // Generar código aleatorio criptográfico
            var code = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
            _recoveryCodes[username] = (code, DateTime.UtcNow.AddMinutes(10));

            var telegram = _serviceProvider.GetRequiredService<TelegramService>();
            await telegram.SendDirectAsync(user.TelegramChatId,
                $"🔑 <b>Código de recuperación RadioLogger</b>\n\nCódigo: <b>{code}</b>\n\nVálido por 10 minutos.");

            return (true, "Si el usuario existe y tiene Telegram configurado, recibirá el código.");
        }

        private static string? GetStoredRecoveryCode(string username)
        {
            if (_recoveryCodes.TryGetValue(username, out var stored) && stored.expires > DateTime.UtcNow)
                return stored.code;
            return null;
        }

        // Auditoría
        public async Task LogActionAsync(string username, string action, string? detail = null, string? ipAddress = null)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
                db.AuditLog.Add(new AuditEntry
                {
                    Username = username,
                    Action = action,
                    Detail = detail,
                    IpAddress = ipAddress
                });
                await db.SaveChangesAsync();
            }
            catch { }
        }

        public async Task<List<AuditEntry>> GetAuditLogAsync(int count = 100, string? username = null)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();

            var query = db.AuditLog.AsQueryable();
            if (!string.IsNullOrEmpty(username))
                query = query.Where(a => a.Username == username);

            return await query.OrderByDescending(a => a.Timestamp).Take(count).ToListAsync();
        }

        /// <summary>
        /// Migra hash viejo SHA256 a BCrypt (para compatibilidad con admin seed).
        /// </summary>
        public async Task MigrateLegacyHashesAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();

            var users = await db.Users.ToListAsync();
            foreach (var user in users)
            {
                // BCrypt hashes empiezan con "$2". SHA256 hashes son hex de 64 chars.
                if (!user.PasswordHash.StartsWith("$2") && user.PasswordHash.Length == 64)
                {
                    // Es un hash SHA256 legacy — no podemos migrar sin la contraseña
                    // Pero para el admin seed conocemos la contraseña
                    if (user.Username == "admin" && user.PasswordHash == "48e7a03e4ef52df853e4e1e87a58c69d1a41a03dcb18590355e36aef4cc5b91c")
                    {
                        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin123!");
                        await db.SaveChangesAsync();
                        Console.WriteLine("[AUTH] Migrado hash del admin seed a BCrypt");
                    }
                }
            }
        }
    }
}
