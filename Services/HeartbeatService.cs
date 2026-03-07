using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Timers;

namespace RadioLogger.Services
{
    public class HeartbeatService
    {
        private readonly ConfigManager _configManager;
        private readonly System.Timers.Timer _timer;
        private readonly HttpClient _httpClient;

        public HeartbeatService(ConfigManager configManager)
        {
            _configManager = configManager;
            _httpClient = new HttpClient();
            
            _timer = new System.Timers.Timer();
            _timer.Elapsed += async (s, e) => await SendHeartbeat();
        }

        public void Start()
        {
            _timer.Interval = _configManager.CurrentSettings.HeartbeatIntervalSeconds * 1000;
            _timer.Start();
            // Send one immediately
            Task.Run(SendHeartbeat);
        }

        public void Stop()
        {
            _timer.Stop();
        }

        private async Task SendHeartbeat()
        {
            try
            {
                var url = _configManager.CurrentSettings.HeartbeatUrl;
                // Simple fire and forget ping or JSON payload
                // For now, just a GET to prove life
                // In production, you'd send a POST with status
                
                // Example payload construction could go here
                
                // await _httpClient.GetAsync(url);
                
                // Stub implementation
                System.Diagnostics.Debug.WriteLine($"[Heartbeat] Sent to {url} at {DateTime.Now}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Heartbeat Error] {ex.Message}");
            }
        }
    }
}
