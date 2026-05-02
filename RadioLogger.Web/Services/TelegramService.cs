using System.Text;
using System.Text.Json;

namespace RadioLogger.Web.Services
{
    public class TelegramService
    {
        private readonly IHttpClientFactory _httpFactory;
        private readonly IConfiguration _config;
        private readonly DashboardLogService _log;

        public TelegramService(IHttpClientFactory httpFactory, IConfiguration config, DashboardLogService log)
        {
            _httpFactory = httpFactory;
            _config = config;
            _log = log;
        }

        public async Task SendAlertAsync(string message)
        {
            var token = Environment.GetEnvironmentVariable("TELEGRAM_TOKEN") ?? _config["Telegram:Token"];
            var chatId = Environment.GetEnvironmentVariable("TELEGRAM_CHATID") ?? _config["Telegram:ChatId"];

            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrEmpty(chatId)) return;

            try
            {
                var client = _httpFactory.CreateClient();
                string url = $"https://api.telegram.org/bot{token}/sendMessage";

                var payload = new { chat_id = chatId, text = message, parse_mode = "HTML" };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(url, content);
                if (!response.IsSuccessStatusCode)
                    _log.LogWarning("TelegramService", $"SendAlert falló: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _log.LogError("TelegramService", $"Error en SendAlert: {ex.Message}", ex);
            }
        }

        public async Task SendDirectAsync(string chatId, string message)
        {
            var token = Environment.GetEnvironmentVariable("TELEGRAM_TOKEN") ?? _config["Telegram:Token"];
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrEmpty(chatId)) return;

            try
            {
                var client = _httpFactory.CreateClient();
                string url = $"https://api.telegram.org/bot{token}/sendMessage";
                var payload = new { chat_id = chatId, text = message, parse_mode = "HTML" };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                await client.PostAsync(url, content);
            }
            catch (Exception ex)
            {
                _log.LogError("TelegramService", $"Error en SendDirect a {chatId}: {ex.Message}", ex);
            }
        }
    }
}