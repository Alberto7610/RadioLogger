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
        private readonly DashboardLogService _log;

        // Rate limiting: username → (failCount, lockUntil)
        private static readonly ConcurrentDictionary<string, (int fails, DateTime lockUntil)> _loginAttempts = new();
        private const int MaxAttempts = 5;
        private const int LockoutMinutes = 15;
        private const int MaxDictionaryEntries = 10_000;
        private static DateTime _lastCleanup = DateTime.UtcNow;

        // Códigos de recuperación aleatorios (no predecibles)
        private static readonly ConcurrentDictionary<string, (string code, DateTime expires)> _recoveryCodes = new();

        public AuthService(IServiceProvider serviceProvider, DashboardLogService log)
        {
            _serviceProvider = serviceProvider;
            _log = log;
        }

        /// <summary>
        /// Limpia entradas expiradas de los diccionarios en memoria.
        /// Se ejecuta automáticamente cada 5 minutos en cualquier llamada a ValidateLogin.
        /// </summary>
        private static void CleanupExpiredEntries()
        {
            if ((DateTime.UtcNow - _lastCleanup).TotalMinutes < 5) return;
            _lastCleanup = DateTime.UtcNow;

            // Limpiar lockouts expirados (lockUntil ya pasó Y no tienen lock activo)
            var expiredLogins = _loginAttempts
                .Where(kv => kv.Value.lockUntil < DateTime.UtcNow && kv.Value.lockUntil != DateTime.MinValue)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var key in expiredLogins)
                _loginAttempts.TryRemove(key, out _);

            // Limpiar códigos de recuperación expirados
            var expiredCodes = _recoveryCodes
                .Where(kv => kv.Value.expires < DateTime.UtcNow)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var key in expiredCodes)
                _recoveryCodes.TryRemove(key, out _);

            // Protección contra ataque masivo: si hay demasiadas entradas, purgar las más viejas
            if (_loginAttempts.Count > MaxDictionaryEntries)
                _loginAttempts.Clear();
        }

        public async Task<(AppUser? user, string? error)> ValidateLoginAsync(string username, string password)
        {
            CleanupExpiredEntries();

            // Rate limiting check
            var wasLockedBefore = false;
            if (_loginAttempts.TryGetValue(username, out var attempt))
            {
                if (attempt.lockUntil > DateTime.UtcNow)
                {
                    var minutesLeft = (int)(attempt.lockUntil - DateTime.UtcNow).TotalMinutes + 1;
                    return (null, $"Cuenta bloqueada. Intenta en {minutesLeft} minutos o restablece tu contraseña.");
                }
                // Lock expirado pero el contador sigue al máximo: estado frágil de "1 intento"
                if (attempt.fails >= MaxAttempts)
                    wasLockedBefore = true;
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

            // Si ya venía de un lock previo, mensaje específico (no engañar con "5 intentos")
            if (wasLockedBefore)
                return (null, $"Cuenta bloqueada nuevamente por {LockoutMinutes} minutos. Por favor restablece tu contraseña.");

            if (newFails >= MaxAttempts)
                return (null, $"Demasiados intentos. Cuenta bloqueada por {LockoutMinutes} minutos. Tras la espera solo tendrás 1 intento — considera restablecer tu contraseña.");

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

        public async Task<(bool success, string message)> CreateUserAsync(string username, string password, string displayName, string email, string role, string? telegramChatId)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();

            if (await db.Users.AnyAsync(u => u.Username == username))
                return (false, "El usuario ya existe");

            if (password.Length < 6)
                return (false, "La contraseña debe tener al menos 6 caracteres");

            if (!IsValidEmail(email))
                return (false, "El email no es válido");

            var user = new AppUser
            {
                Username = username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                DisplayName = displayName,
                Email = email.Trim(),
                Role = role,
                TelegramChatId = telegramChatId,
                IsActive = true
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();
            return (true, "Usuario creado");
        }

        public async Task<(bool success, string message)> UpdateUserAsync(int id, string displayName, string email, string role, bool isActive, string? telegramChatId)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();

            var user = await db.Users.FindAsync(id);
            if (user == null) return (false, "Usuario no encontrado");

            if (!IsValidEmail(email))
                return (false, "El email no es válido");

            user.DisplayName = displayName;
            user.Email = email.Trim();
            user.Role = role;
            user.IsActive = isActive;
            user.TelegramChatId = telegramChatId;
            await db.SaveChangesAsync();
            return (true, "Usuario actualizado");
        }

        /// <summary>
        /// Activa o desactiva un usuario (soft-delete reversible).
        /// Protecciones: no permite que un usuario se desactive a sí mismo,
        /// ni que se desactive al último Administrador activo.
        /// </summary>
        public async Task<(bool success, string message)> ToggleUserStatusAsync(int id, string requestingUsername)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();

            var user = await db.Users.FindAsync(id);
            if (user == null) return (false, "Usuario no encontrado");

            if (string.Equals(user.Username, requestingUsername, StringComparison.OrdinalIgnoreCase))
                return (false, "No puedes desactivarte a ti mismo");

            // Si vamos a desactivar y este es el último admin activo, bloquear
            if (user.IsActive && user.Role == "Administrador")
            {
                var activeAdmins = await db.Users.CountAsync(u => u.Role == "Administrador" && u.IsActive);
                if (activeAdmins <= 1)
                    return (false, "No se puede desactivar al último Administrador activo");
            }

            user.IsActive = !user.IsActive;
            await db.SaveChangesAsync();
            return (true, user.IsActive ? "Usuario activado" : "Usuario desactivado");
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

        /// <summary>
        /// Envía código de recuperación por email (default) o telegram (si method == "telegram").
        /// Mensaje de respuesta es siempre genérico para no revelar si el usuario existe.
        /// </summary>
        public async Task<(bool success, string message)> SendRecoveryCodeAsync(string username, string method = "email")
        {
            const string genericResponse = "Si el usuario existe y el método elegido está configurado, recibirás el código.";

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();

            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username && u.IsActive);
            if (user == null) return (true, genericResponse);

            // Generar código aleatorio criptográfico
            var code = RandomNumberGenerator.GetInt32(100000, 999999).ToString();

            bool sent = false;

            if (method == "telegram")
            {
                if (!string.IsNullOrEmpty(user.TelegramChatId))
                {
                    var telegram = _serviceProvider.GetRequiredService<TelegramService>();
                    await telegram.SendDirectAsync(user.TelegramChatId,
                        $"🔑 <b>Código de recuperación RadioLogger</b>\n\nCódigo: <b>{code}</b>\n\nVálido por 10 minutos.");
                    sent = true; // SendDirectAsync no devuelve estado, asumimos enviado
                }
            }
            else // email (default)
            {
                if (!string.IsNullOrEmpty(user.Email))
                {
                    var email = _serviceProvider.GetRequiredService<EmailService>();
                    sent = await email.SendRecoveryCodeAsync(user.Email, user.Username, code);
                }
            }

            // Solo guardar el código si se envió exitosamente
            if (sent)
                _recoveryCodes[username] = (code, DateTime.UtcNow.AddMinutes(10));

            return (true, genericResponse);
        }

        /// <summary>
        /// Indica qué métodos de recuperación tiene configurados el usuario (email, telegram).
        /// Devuelve flags genéricos sin revelar si el usuario existe.
        /// </summary>
        public async Task<(bool hasEmail, bool hasTelegram)> GetRecoveryMethodsAsync(string username)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();

            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username && u.IsActive);
            if (user == null) return (false, false);

            return (!string.IsNullOrEmpty(user.Email), !string.IsNullOrEmpty(user.TelegramChatId));
        }

        private static bool IsValidEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;
            try
            {
                var addr = new System.Net.Mail.MailAddress(email.Trim());
                return addr.Address == email.Trim();
            }
            catch
            {
                return false;
            }
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

                // Si el llamador no pasó IP, intentar capturarla del HttpContext actual
                ipAddress ??= GetClientIpAddress();

                db.AuditLog.Add(new AuditEntry
                {
                    Username = username,
                    Action = action,
                    Detail = detail,
                    IpAddress = ipAddress
                });
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _log.LogError("AuthService", $"Error registrando auditoría: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Obtiene la IP real del cliente respetando proxies (Cloudflare, IIS).
        /// Prioriza CF-Connecting-IP (Cloudflare) > X-Forwarded-For > RemoteIpAddress.
        /// </summary>
        private string? GetClientIpAddress()
        {
            try
            {
                var accessor = _serviceProvider.GetService<IHttpContextAccessor>();
                var ctx = accessor?.HttpContext;
                if (ctx == null) return null;

                // 1) Cloudflare envía la IP real del visitante en este header
                var cf = ctx.Request.Headers["CF-Connecting-IP"].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(cf)) return cf.Trim();

                // 2) X-Forwarded-For (puede venir de IIS/proxy, primer valor es el cliente)
                var xff = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(xff))
                    return xff.Split(',').First().Trim();

                // 3) IP directa de la conexión
                return ctx.Connection.RemoteIpAddress?.ToString();
            }
            catch
            {
                return null;
            }
        }

        public async Task<List<AuditEntry>> GetAuditLogAsync(
            int count = 200,
            string? username = null,
            string? action = null,
            DateTime? fromUtc = null,
            DateTime? toUtc = null)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();

            var query = db.AuditLog.AsQueryable();

            if (!string.IsNullOrEmpty(username))
                query = query.Where(a => a.Username == username);

            if (!string.IsNullOrEmpty(action))
                query = query.Where(a => a.Action == action);

            if (fromUtc.HasValue)
                query = query.Where(a => a.Timestamp >= fromUtc.Value);

            if (toUtc.HasValue)
                query = query.Where(a => a.Timestamp <= toUtc.Value);

            return await query.OrderByDescending(a => a.Timestamp).Take(count).ToListAsync();
        }

        /// <summary>
        /// Devuelve la lista distinta de acciones registradas para poblar filtros.
        /// </summary>
        public async Task<List<string>> GetDistinctAuditActionsAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
            return await db.AuditLog
                .Select(a => a.Action)
                .Distinct()
                .OrderBy(a => a)
                .ToListAsync();
        }
    }
}
