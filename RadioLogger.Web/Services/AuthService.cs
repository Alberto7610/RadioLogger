using Microsoft.EntityFrameworkCore;
using RadioLogger.Shared.Models;
using RadioLogger.Web.Data;
using System.Security.Cryptography;
using System.Text;

namespace RadioLogger.Web.Services
{
    public class AuthService
    {
        private readonly IServiceProvider _serviceProvider;

        public AuthService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task<AppUser?> ValidateLoginAsync(string username, string password)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();

            var hash = ComputeHash(password);
            var user = await db.Users.FirstOrDefaultAsync(u =>
                u.Username == username && u.PasswordHash == hash && u.IsActive);

            if (user != null)
            {
                user.LastLogin = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }

            return user;
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
                PasswordHash = ComputeHash(password),
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

        public async Task<(bool success, string message)> ChangePasswordAsync(int userId, string newPassword)
        {
            if (newPassword.Length < 6)
                return (false, "La contraseña debe tener al menos 6 caracteres");

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();

            var user = await db.Users.FindAsync(userId);
            if (user == null) return (false, "Usuario no encontrado");

            user.PasswordHash = ComputeHash(newPassword);
            await db.SaveChangesAsync();
            return (true, "Contraseña actualizada");
        }

        public async Task<(bool success, string message)> ResetPasswordWithCodeAsync(string username, string code, string newPassword)
        {
            // Validar código temporal (vigente 10 minutos)
            var expectedCode = GenerateRecoveryCode(username);
            if (!string.Equals(code.Trim(), expectedCode, StringComparison.OrdinalIgnoreCase))
                return (false, "Código inválido o expirado");

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();

            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username && u.IsActive);
            if (user == null) return (false, "Usuario no encontrado");

            if (newPassword.Length < 6)
                return (false, "La contraseña debe tener al menos 6 caracteres");

            user.PasswordHash = ComputeHash(newPassword);
            await db.SaveChangesAsync();
            return (true, "Contraseña restablecida");
        }

        public async Task<(bool success, string message)> SendRecoveryCodeAsync(string username)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();

            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username && u.IsActive);
            if (user == null)
                return (false, "Usuario no encontrado");

            if (string.IsNullOrEmpty(user.TelegramChatId))
                return (false, "Este usuario no tiene Telegram configurado. Contacta al administrador.");

            var code = GenerateRecoveryCode(username);
            var telegram = _serviceProvider.GetRequiredService<TelegramService>();
            await telegram.SendDirectAsync(user.TelegramChatId,
                $"🔑 <b>Código de recuperación RadioLogger</b>\n\nUsuario: <b>{username}</b>\nCódigo: <b>{code}</b>\n\nVálido por 10 minutos.");

            return (true, "Código enviado a tu Telegram");
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
            catch { } // No bloquear operación principal por fallo de auditoría
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

        public static string ComputeHash(string input)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexStringLower(bytes);
        }

        /// <summary>
        /// Genera código de 6 dígitos válido por ventana de 10 minutos.
        /// </summary>
        private static string GenerateRecoveryCode(string username)
        {
            var window = DateTime.UtcNow.ToString("yyyyMMddHHmm")[..11]; // Ventana de 10 min
            var input = $"RL-RECOVERY|{username}|{window}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            var num = BitConverter.ToUInt32(hash, 0) % 1000000;
            return num.ToString("D6");
        }
    }
}
