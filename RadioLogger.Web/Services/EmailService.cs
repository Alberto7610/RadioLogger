using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace RadioLogger.Web.Services
{
    public class EmailService
    {
        private readonly IHttpClientFactory _httpFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IHttpClientFactory httpFactory, IConfiguration config, ILogger<EmailService> logger)
        {
            _httpFactory = httpFactory;
            _config = config;
            _logger = logger;
        }

        public async Task<bool> SendRecoveryCodeAsync(string toEmail, string username, string code)
        {
            var apiKey = Environment.GetEnvironmentVariable("RESEND_API_KEY") ?? _config["Resend:ApiKey"];
            var fromEmail = Environment.GetEnvironmentVariable("RESEND_FROM") ?? _config["Resend:From"] ?? "recovery@cloudradiologger.com";
            var fromName = _config["Resend:FromName"] ?? "CloudRadioLogger";

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogError("[EMAIL] RESEND_API_KEY no está configurado. No se puede enviar el código.");
                return false;
            }

            var subject = "Código de recuperación de contraseña — CloudRadioLogger";
            var html = BuildRecoveryHtml(username, code);
            var text = $"Hola {username},\n\nTu código de recuperación es: {code}\n\nVálido por 10 minutos.\n\nSi no solicitaste este código, ignora este mensaje.\n\n— CloudRadioLogger";

            var payload = new
            {
                from = $"{fromName} <{fromEmail}>",
                to = new[] { toEmail },
                subject,
                html,
                text
            };

            try
            {
                var client = _httpFactory.CreateClient();
                using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                var resp = await client.SendAsync(req);
                var body = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogError("[EMAIL] Resend respondió {Status}: {Body}", resp.StatusCode, body);
                    return false;
                }

                _logger.LogInformation("[EMAIL] Código de recuperación enviado a {Email}", toEmail);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EMAIL] Error enviando código a {Email}", toEmail);
                return false;
            }
        }

        private static string BuildRecoveryHtml(string username, string code)
        {
            return $@"<!DOCTYPE html>
<html>
<head><meta charset=""utf-8""></head>
<body style=""font-family: -apple-system, Segoe UI, Roboto, sans-serif; background: #f5f7fa; padding: 40px 20px; margin: 0;"">
  <div style=""max-width: 480px; margin: 0 auto; background: #ffffff; border-radius: 12px; padding: 40px; box-shadow: 0 2px 12px rgba(0,0,0,0.06);"">
    <div style=""text-align: center; margin-bottom: 30px;"">
      <div style=""font-size: 22px; font-weight: 700; color: #0d1018; letter-spacing: 2px;"">CLOUD<span style=""color: #4d9eff;"">RADIOLOGGER</span></div>
      <div style=""font-size: 10px; color: #6b7a94; letter-spacing: 3px; margin-top: 4px;"">ENTERPRISE V2.0</div>
    </div>

    <h2 style=""color: #0d1018; font-size: 18px; margin: 0 0 16px 0;"">Recuperación de contraseña</h2>
    <p style=""color: #4a5568; font-size: 14px; line-height: 1.6;"">Hola <strong>{System.Net.WebUtility.HtmlEncode(username)}</strong>,</p>
    <p style=""color: #4a5568; font-size: 14px; line-height: 1.6;"">Recibimos una solicitud para restablecer tu contraseña. Usa este código en la pantalla de recuperación:</p>

    <div style=""background: #0d1018; color: #10d98a; font-size: 32px; font-weight: 700; letter-spacing: 12px; text-align: center; padding: 20px; border-radius: 8px; margin: 24px 0; font-family: 'Courier New', monospace;"">
      {code}
    </div>

    <p style=""color: #6b7a94; font-size: 12px; line-height: 1.6;"">El código es válido por <strong>10 minutos</strong>. Si no solicitaste esta recuperación, puedes ignorar este correo de forma segura — tu contraseña no será modificada.</p>

    <hr style=""border: none; border-top: 1px solid #e2e8f0; margin: 30px 0;"">

    <p style=""color: #94a3b8; font-size: 11px; text-align: center; margin: 0;"">Este es un correo automático. Por favor no respondas.</p>
  </div>
</body>
</html>";
        }
    }
}
