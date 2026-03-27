using RadioLogger.Models;
using RadioLogger.Shared.Models;
using Serilog;
using System;
using System.Security.Cryptography;
using System.Text;

namespace RadioLogger.Services
{
    public enum LicenseStatus
    {
        Valid,          // Licencia activa y vigente
        GracePeriod,    // Expirada pero dentro de los 30 días de gracia offline
        Expired,        // Expirada y sin gracia
        NoLicense       // No hay licencia
    }

    public class LicenseService
    {
        private static readonly ILogger _log = AppLog.For<LicenseService>();
        private readonly AppSettings _settings;
        private readonly ConfigManager _configManager;

        // Secreto para generar/validar códigos temporales (debe coincidir con Dashboard)
        internal const string OfflineSecret = "RL2026-OFFLINE-KEY";
        private const int GraceDaysOffline = 30;

        public LicenseStatus CurrentStatus { get; private set; } = LicenseStatus.NoLicense;
        public LocalLicense? CurrentLicense => _settings.License;
        public int DaysRemaining { get; private set; }
        public string StatusMessage { get; private set; } = "";

        public LicenseService(ConfigManager configManager)
        {
            _configManager = configManager;
            _settings = configManager.CurrentSettings;
            Validate();
        }

        /// <summary>
        /// Valida la licencia local al iniciar la app.
        /// </summary>
        public LicenseStatus Validate()
        {
            var lic = _settings.License;

            if (lic == null || string.IsNullOrEmpty(lic.Key) || !lic.IsValid)
            {
                CurrentStatus = LicenseStatus.NoLicense;
                DaysRemaining = 0;
                StatusMessage = "Sin licencia — esperando activación del Dashboard";
                _log.Warning("Sin licencia activa");
                return CurrentStatus;
            }

            var now = DateTime.UtcNow;
            var daysUntilExpiry = (lic.ExpirationDate - now).TotalDays;

            if (daysUntilExpiry > 0)
            {
                // Licencia vigente
                CurrentStatus = LicenseStatus.Valid;
                DaysRemaining = (int)Math.Ceiling(daysUntilExpiry);
                StatusMessage = $"Licencia {lic.LicenseType} — {DaysRemaining} días restantes";

                if (DaysRemaining <= 15)
                    _log.Warning("Licencia expira en {Days} días", DaysRemaining);

                return CurrentStatus;
            }

            // Licencia expirada — verificar gracia offline
            var daysSinceLastCheck = (now - lic.LastServerCheck).TotalDays;
            var daysSinceExpiry = -daysUntilExpiry;

            if (daysSinceExpiry <= GraceDaysOffline)
            {
                CurrentStatus = LicenseStatus.GracePeriod;
                DaysRemaining = GraceDaysOffline - (int)daysSinceExpiry;
                StatusMessage = $"Licencia expirada — gracia offline: {DaysRemaining} días restantes";
                _log.Warning("Licencia en período de gracia offline ({DaysLeft} días)", DaysRemaining);
                return CurrentStatus;
            }

            // Sin gracia
            CurrentStatus = LicenseStatus.Expired;
            DaysRemaining = 0;
            StatusMessage = "Licencia expirada — grabación bloqueada";
            _log.Error("Licencia expirada. Grabación bloqueada.");
            return CurrentStatus;
        }

        /// <summary>
        /// ¿Puede grabar esta máquina?
        /// </summary>
        public bool CanRecord()
        {
            return CurrentStatus == LicenseStatus.Valid || CurrentStatus == LicenseStatus.GracePeriod;
        }

        /// <summary>
        /// ¿Está dentro del máximo de slots permitidos?
        /// </summary>
        public bool CanAddStation(int currentActiveStations)
        {
            if (!CanRecord()) return false;
            var lic = _settings.License;
            if (lic == null) return false;
            return currentActiveStations < lic.MaxSlots;
        }

        /// <summary>
        /// Recibe licencia del Dashboard vía SignalR y la guarda localmente.
        /// </summary>
        public void ActivateFromServer(LocalLicense license)
        {
            license.LastServerCheck = DateTime.UtcNow;
            license.IsValid = true;
            _settings.License = license;
            _configManager.Save();

            Validate();
            _log.Information("Licencia activada desde Dashboard: {Type} ({Key}), vence {Expiry:dd/MM/yyyy}, slots: {Slots}",
                license.LicenseType, license.Key, license.ExpirationDate, license.MaxSlots);
        }

        /// <summary>
        /// Actualiza LastServerCheck cuando el servidor confirma la licencia.
        /// </summary>
        public void ConfirmServerCheck()
        {
            if (_settings.License != null)
            {
                _settings.License.LastServerCheck = DateTime.UtcNow;
                _configManager.Save();
            }
        }

        /// <summary>
        /// Valida un código temporal offline y extiende 30 días.
        /// </summary>
        public (bool success, string message) ApplyOfflineCode(string code, string hardwareId)
        {
            if (string.IsNullOrWhiteSpace(code))
                return (false, "Código vacío");

            // El código válido es: primeros 8 chars de SHA256(HardwareId + Secret + YYYYMM)
            var now = DateTime.UtcNow;

            // Aceptar código del mes actual y del anterior (por si se generó a fin de mes)
            for (int monthOffset = 0; monthOffset >= -1; monthOffset--)
            {
                var checkDate = now.AddMonths(monthOffset);
                var expected = GenerateOfflineCode(hardwareId, checkDate);
                if (string.Equals(code.Trim(), expected, StringComparison.OrdinalIgnoreCase))
                {
                    // Código válido — extender 30 días desde hoy
                    if (_settings.License == null)
                    {
                        _settings.License = new LocalLicense
                        {
                            Key = "OFFLINE-EXTENSION",
                            LicenseType = "EMERGENCIA",
                            MaxSlots = 4,
                            IsValid = true
                        };
                    }

                    _settings.License.ExpirationDate = DateTime.UtcNow.AddDays(30);
                    _settings.License.LastServerCheck = DateTime.UtcNow;
                    _settings.License.IsValid = true;
                    _configManager.Save();

                    Validate();
                    _log.Information("Código temporal offline aplicado. Licencia extendida 30 días hasta {Date:dd/MM/yyyy}",
                        _settings.License.ExpirationDate);

                    return (true, $"Licencia extendida hasta {_settings.License.ExpirationDate.ToLocalTime():dd/MM/yyyy}");
                }
            }

            _log.Warning("Código temporal offline inválido: {Code}", code);
            return (false, "Código inválido o expirado");
        }

        /// <summary>
        /// Genera código temporal para un HardwareId (usado por Dashboard y validación local).
        /// </summary>
        public static string GenerateOfflineCode(string hardwareId, DateTime date)
        {
            var input = $"{hardwareId}|{OfflineSecret}|{date:yyyyMM}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexStringLower(hash)[..8].ToUpperInvariant();
        }
    }
}
