using System;
using System.Net.Http;
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
            var token = _config["Telegram:Token"];
            var chatId = _config["Telegram:ChatId"];

            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrEmpty(chatId)) return;

            try
            {
                // Format message for Telegram API
                string url = $"https://api.telegram.org/bot{token}/sendMessage?chat_id={chatId}&text={Uri.EscapeDataString(message)}&parse_mode=HTML";
                
                var response = await _httpClient.GetAsync(url);
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