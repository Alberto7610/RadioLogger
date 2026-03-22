using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RadioLogger.Services
{
    public static class TelegramService
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public static async Task SendAlertAsync(string token, string chatId, string message)
        {
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
                    LogService.Log(LogCategory.NETWORK, $"Falla al enviar Telegram: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                LogService.Log(LogCategory.NETWORK, $"Error conexión Telegram: {ex.Message}");
            }
        }
    }
}