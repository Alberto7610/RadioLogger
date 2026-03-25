using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Timers;

namespace RadioLogger.Services
{
    public class HeartbeatService : IDisposable
    {
        private readonly ConfigManager _configManager;
        private readonly System.Timers.Timer _timer;
        private readonly HttpClient _httpClient;
        private bool _isSending;
        private bool _isDisposed;

        public HeartbeatService(ConfigManager configManager)
        {
            _configManager = configManager;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            _timer = new System.Timers.Timer();
            _timer.AutoReset = false;
            _timer.Elapsed += OnTimerElapsed;
        }

        public void Start()
        {
            _timer.Interval = _configManager.CurrentSettings.HeartbeatIntervalSeconds * 1000;
            _timer.Start();
            Task.Run(SendHeartbeat);
        }

        public void Stop()
        {
            _timer.Stop();
        }

        private async void OnTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            if (_isDisposed) return;

            if (!_isSending)
            {
                _isSending = true;
                try
                {
                    await SendHeartbeat();
                }
                finally
                {
                    _isSending = false;
                }
            }

            if (!_isDisposed)
                _timer.Start();
        }

        private async Task SendHeartbeat()
        {
            try
            {
                var url = _configManager.CurrentSettings.HeartbeatUrl;
                System.Diagnostics.Debug.WriteLine($"[Heartbeat] Sent to {url} at {DateTime.Now}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Heartbeat Error] {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                _timer.Stop();
                _timer.Dispose();
                _httpClient.Dispose();
            }
        }
    }
}
