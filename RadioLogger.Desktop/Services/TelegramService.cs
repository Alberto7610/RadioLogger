using System;
using System.Net.Http;
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
                // Format message for Telegram API
                string url = $"https://api.telegram.org/bot{token}/sendMessage?chat_id={chatId}&text={Uri.EscapeDataString(message)}&parse_mode=HTML";
                
                var response = await _httpClient.GetAsync(url);
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