using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RadioLogger.Shared.Models;
using RadioLogger.Web.Data;
using RadioLogger.Web.Hubs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace RadioLogger.Web.Services
{
    public class LicenseManager
    {
        private readonly IServiceProvider _serviceProvider;
        private const string OfflineSecret = "RL2026-OFFLINE-KEY"; // Debe coincidir con WPF

        public LicenseManager(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Get all licenses.
        /// </summary>
        public async Task<List<License>> GetAllAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
            return await db.Licenses.OrderByDescending(l => l.IsActive).ThenBy(l => l.ExpirationDate).ToListAsync();
        }

        /// <summary>
        /// Get license for a specific machine.
        /// </summary>
        public async Task<License?> GetByMachineAsync(string machineId)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
            return await db.Licenses.FirstOrDefaultAsync(l => l.MachineId == machineId && l.IsActive);
        }

        /// <summary>
        /// Create a new license and send it to the WPF client via SignalR.
        /// </summary>
        public async Task<License> CreateAsync(string licenseType, string machineId, string hardwareId,
            string clientName, int maxSlots, int durationDays)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();

            // Desactivar licencia anterior para esta máquina
            var existing = await db.Licenses.Where(l => l.MachineId == machineId && l.IsActive).ToListAsync();
            foreach (var old in existing)
                old.IsActive = false;

            var license = new License
            {
                Key = GenerateKey(),
                LicenseType = licenseType,
                ClientName = clientName,
                MachineId = machineId,
                HardwareId = hardwareId,
                MaxSlots = maxSlots,
                StartDate = DateTime.UtcNow,
                ExpirationDate = DateTime.UtcNow.AddDays(durationDays),
                IsActive = true
            };

            db.Licenses.Add(license);
            await db.SaveChangesAsync();

            // Enviar al WPF vía SignalR
            await SendLicenseToClient(license);

            return license;
        }

        /// <summary>
        /// Renew an existing license.
        /// </summary>
        public async Task<bool> RenewAsync(int licenseId, int durationDays)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();

            var license = await db.Licenses.FindAsync(licenseId);
            if (license == null) return false;

            // Si la licencia aún no ha expirado, extender desde la fecha de expiración actual
            var baseDate = license.ExpirationDate > DateTime.UtcNow ? license.ExpirationDate : DateTime.UtcNow;
            license.ExpirationDate = baseDate.AddDays(durationDays);
            license.IsActive = true;
            await db.SaveChangesAsync();

            await SendLicenseToClient(license);
            return true;
        }

        /// <summary>
        /// Revoke a license.
        /// </summary>
        public async Task<bool> RevokeAsync(int licenseId)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();

            var license = await db.Licenses.FindAsync(licenseId);
            if (license == null) return false;

            license.IsActive = false;
            await db.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// Convert license type (e.g. DEMO → PLAZA).
        /// </summary>
        public async Task<bool> ConvertAsync(int licenseId, string newType, int maxSlots, int durationDays)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();

            var license = await db.Licenses.FindAsync(licenseId);
            if (license == null) return false;

            license.LicenseType = newType;
            license.MaxSlots = maxSlots;
            license.StartDate = DateTime.UtcNow;
            license.ExpirationDate = DateTime.UtcNow.AddDays(durationDays);
            license.IsActive = true;
            await db.SaveChangesAsync();

            await SendLicenseToClient(license);
            return true;
        }

        /// <summary>
        /// Update last check-in from WPF client.
        /// </summary>
        public async Task CheckInAsync(string machineId)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();

            var license = await db.Licenses.FirstOrDefaultAsync(l => l.MachineId == machineId && l.IsActive);
            if (license != null)
            {
                license.LastCheckIn = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Generate offline code for a hardware ID.
        /// </summary>
        public static string GenerateOfflineCode(string hardwareId)
        {
            var input = $"{hardwareId}|{OfflineSecret}|{DateTime.UtcNow:yyyyMM}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexStringLower(hash)[..8].ToUpperInvariant();
        }

        /// <summary>
        /// Count active stations for a machine (from MonitoringService).
        /// </summary>
        public int GetActiveStationCount(string machineId)
        {
            var monitoring = _serviceProvider.GetRequiredService<MonitoringService>();
            return monitoring.GetActiveStations().Count(s => s.MachineId == machineId && s.IsRecording);
        }

        /// <summary>
        /// Send license to WPF client via SignalR.
        /// </summary>
        private async Task SendLicenseToClient(License license)
        {
            try
            {
                var hubContext = _serviceProvider.GetRequiredService<IHubContext<RadioHub>>();
                var connectionId = RadioHub.GetConnectionId(license.MachineId);

                if (connectionId != null)
                {
                    var localLicense = new LocalLicense
                    {
                        Key = license.Key,
                        LicenseType = license.LicenseType,
                        MaxSlots = license.MaxSlots,
                        ExpirationDate = license.ExpirationDate,
                        LastServerCheck = DateTime.UtcNow,
                        IsValid = license.IsActive
                    };

                    await hubContext.Clients.Client(connectionId).SendAsync("ReceiveLicense", localLicense);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LICENSE] Error sending license to client: {ex.Message}");
            }
        }

        private static string GenerateKey()
        {
            var bytes = RandomNumberGenerator.GetBytes(6);
            var hex = Convert.ToHexStringLower(bytes).ToUpperInvariant();
            return $"RL-{DateTime.UtcNow:yyyy}-{hex[..4]}-{hex[4..8]}";
        }
    }
}
