using RadioLogger.Shared.Models;
using RadioLogger.Web.Data;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace RadioLogger.Web.Services
{
    public class PairingService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ConcurrentDictionary<string, PairingCode> _activeCodes = new();

        // Cache of valid API keys for fast lookup (refreshed from DB)
        private volatile ConcurrentDictionary<string, ApiKey> _keyCache = new();
        private volatile int _cacheRefreshing; // 0 = idle, 1 = refreshing (atomic guard)
        private DateTime _lastCacheRefresh = DateTime.MinValue;

        // Rate limiting for Pair() attempts per connection
        private readonly ConcurrentDictionary<string, (int Attempts, DateTime WindowStart)> _pairAttempts = new();

        public PairingService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
            _ = RefreshKeyCacheAsync();
        }

        /// <summary>
        /// Generates a 6-digit pairing code valid for 5 minutes.
        /// </summary>
        public string GeneratePairingCode()
        {
            // Clean expired codes
            var expired = _activeCodes.Where(kv => kv.Value.ExpiresAt < DateTime.UtcNow).Select(kv => kv.Key).ToList();
            foreach (var key in expired) _activeCodes.TryRemove(key, out _);

            string code = RandomNumberGenerator.GetInt32(100000, 999999).ToString();

            // Ensure unique
            while (_activeCodes.ContainsKey(code))
                code = RandomNumberGenerator.GetInt32(100000, 999999).ToString();

            _activeCodes[code] = new PairingCode
            {
                Code = code,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(5)
            };

            return code;
        }

        /// <summary>
        /// Checks rate limit for pairing attempts. Max 5 attempts per 5 minutes per connection.
        /// </summary>
        public bool IsRateLimited(string connectionId)
        {
            if (_pairAttempts.TryGetValue(connectionId, out var state))
            {
                if ((DateTime.UtcNow - state.WindowStart).TotalMinutes > 5)
                {
                    _pairAttempts[connectionId] = (1, DateTime.UtcNow);
                    return false;
                }
                if (state.Attempts >= 5) return true;
                _pairAttempts[connectionId] = (state.Attempts + 1, state.WindowStart);
            }
            else
            {
                _pairAttempts[connectionId] = (1, DateTime.UtcNow);
            }
            return false;
        }

        /// <summary>
        /// Validates a pairing code and creates a permanent API key for the machine.
        /// Uses TryRemove as atomic operation to prevent double-redemption.
        /// </summary>
        public async Task<PairingResult> RedeemCodeAsync(string code, string machineId, string machineName)
        {
            // Atomic removal prevents race condition — only one caller gets the code
            if (!_activeCodes.TryRemove(code, out var pairingCode))
                return new PairingResult { Success = false, Error = "Código inválido" };

            if (pairingCode.ExpiresAt < DateTime.UtcNow)
                return new PairingResult { Success = false, Error = "Código expirado" };

            // Generate a secure API key
            var apiKeyString = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

            // Store in DB
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();

            // Deactivate any previous key for this machine
            var existingKeys = db.ApiKeys.Where(k => k.MachineId == machineId && k.IsActive);
            foreach (var existing in existingKeys)
                existing.IsActive = false;

            var apiKey = new ApiKey
            {
                Key = apiKeyString,
                MachineId = machineId,
                MachineName = machineName,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            db.ApiKeys.Add(apiKey);
            await db.SaveChangesAsync();

            // Update cache
            _keyCache[apiKeyString] = apiKey;

            return new PairingResult { Success = true, ApiKey = apiKeyString };
        }

        /// <summary>
        /// Validates an API key. Returns true if the key exists and is active.
        /// </summary>
        public async Task<bool> ValidateKeyAsync(string? apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) return false;

            // Refresh cache every 60 seconds (only one thread at a time)
            if ((DateTime.UtcNow - _lastCacheRefresh).TotalSeconds > 60)
                await RefreshKeyCacheAsync();

            return _keyCache.TryGetValue(apiKey, out var key) && key.IsActive;
        }

        /// <summary>
        /// Gets all API keys (for admin dashboard).
        /// </summary>
        public async Task<List<ApiKey>> GetAllKeysAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
            return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .ToListAsync(db.ApiKeys.OrderByDescending(k => k.CreatedAt));
        }

        /// <summary>
        /// Revokes an API key.
        /// </summary>
        public async Task RevokeKeyAsync(int keyId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
            var key = await db.ApiKeys.FindAsync(keyId);
            if (key != null)
            {
                key.IsActive = false;
                await db.SaveChangesAsync();
                _keyCache.TryRemove(key.Key, out _);
            }
        }

        /// <summary>
        /// Gets active pairing codes (for admin UI display).
        /// </summary>
        public List<(string Code, DateTime ExpiresAt)> GetActiveCodes()
        {
            var now = DateTime.UtcNow;
            return _activeCodes.Values
                .Where(c => c.ExpiresAt > now)
                .Select(c => (c.Code, c.ExpiresAt))
                .ToList();
        }

        /// <summary>
        /// Refreshes the key cache from DB using atomic swap (no Clear() race condition).
        /// </summary>
        private async Task RefreshKeyCacheAsync()
        {
            // Prevent concurrent refreshes
            if (Interlocked.CompareExchange(ref _cacheRefreshing, 1, 0) != 0)
                return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
                var activeKeys = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                    .ToListAsync(db.ApiKeys.Where(k => k.IsActive));

                // Atomic swap — no window where cache is empty
                var newCache = new ConcurrentDictionary<string, ApiKey>();
                foreach (var key in activeKeys)
                    newCache[key.Key] = key;

                _keyCache = newCache;
                _lastCacheRefresh = DateTime.UtcNow;
            }
            catch { }
            finally
            {
                Interlocked.Exchange(ref _cacheRefreshing, 0);
            }
        }

        public void CleanupConnection(string connectionId)
        {
            _pairAttempts.TryRemove(connectionId, out _);
        }

        private class PairingCode
        {
            public string Code { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public DateTime ExpiresAt { get; set; }
        }
    }
}
