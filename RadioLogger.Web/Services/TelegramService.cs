using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RadioLogger.Web.Services
{
    public class TelegramService
    {
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly IConfiguration _config;

        public TelegramService(IConfiguration config)
        {
            _config = config;
        }

        public async Task SendAlertAsync(string message)
        {
            // Prefer environment variable, fallback to config
            var token = Environment.GetEnvironmentVariable("TELEGRAM_TOKEN") ?? _config["Telegram:Token"];
            var chatId = Environment.GetEnvironmentVariable("TELEGRAM_CHATID") ?? _config["Telegram:ChatId"];

            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrEmpty(chatId)) return;

            try
            {
                string url = $"https://api.telegram.org/bot{token}/sendMessage";

                var payload = new { chat_id = chatId, text = message, parse_mode = "HTML" };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[TELEGRAM ERROR] Status: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TELEGRAM EXCEPTION] {ex.Message}");
            }
        }
    }
}