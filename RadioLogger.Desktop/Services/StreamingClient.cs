using RadioLogger.Models;
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace RadioLogger.Services
{
    public class StreamingClient : IDisposable
    {
        private TcpClient? _client;
        private NetworkStream? _stream;
        private Thread? _sendThread;
        private bool _isConnected;
        private readonly System.Collections.Concurrent.ConcurrentQueue<byte[]> _bufferQueue = new System.Collections.Concurrent.ConcurrentQueue<byte[]>();
        private readonly StreamingConfig _config;
        private readonly string _stationName;
        
        public event Action<string>? Disconnected;

        public StreamingClient(StreamingConfig config, string stationName)
        {
            _config = config;
            _stationName = stationName;
        }

        public bool Connect()
        {
            try
            {
                DebugLog.Write($"Attempting streaming connection to {_config.Host}:{_config.Port} ({_config.ServerType})");
                _client = new TcpClient();
                _client.Connect(_config.Host, _config.Port);
                _stream = _client.GetStream();

                if (_config.ServerType == "Icecast" || _config.ServerType == "Shoutcast v2")
                {
                    SendHttpHeaders();
                }
                else
                {
                    // "Shoutcast v1" or "Shoutcast" (Legacy)
                    SendShoutcastV1Headers();
                }

                _isConnected = true;
                _sendThread = new Thread(SendLoop) { IsBackground = true };
                _sendThread.Start();
                
                return true;
            }
            catch (Exception ex)
            {
                DebugLog.Write($"Streaming Connection Failed: {ex.Message}");
                // Don't call Disconnect here to avoid event loop if caller handles return false
                _isConnected = false;
                return false;
            }
        }

        private void SendHttpHeaders()
        {
            // HTTP PUT Handshake (Used for Icecast and Shoutcast v2)
            // Auth is typically "source:password", but user can provide "user:pass"
            string authRaw = (_config.Password.StartsWith("source:") || _config.Password.StartsWith("admin:")) 
                             ? _config.Password : $"source:{_config.Password}";
            
            string auth = Convert.ToBase64String(Encoding.UTF8.GetBytes(authRaw));
            
            var sb = new StringBuilder();
            sb.Append($"PUT {_config.MountPoint} HTTP/1.0\r\n");
            sb.Append($"Host: {_config.Host}:{_config.Port}\r\n");
            sb.Append($"Authorization: Basic {auth}\r\n");
            sb.Append($"User-Agent: BASS/2.4\r\n");
            sb.Append($"Content-Type: audio/mpeg\r\n");
            sb.Append($"ice-name: {_stationName}\r\n");
            sb.Append($"ice-public: 0\r\n");
            sb.Append($"ice-bitrate: {_config.Bitrate}\r\n");
            sb.Append($"icy-metadata: 1\r\n");
            sb.Append("\r\n");

            string headerStr = sb.ToString();
            DebugLog.Write($"[HANDSHAKE] Sending Headers:\n{headerStr}");

            byte[] header = Encoding.ASCII.GetBytes(headerStr);
            _stream?.Write(header, 0, header.Length);
            _stream?.Flush();

            // READ SERVER RESPONSE (Crucial for debugging) 
            try 
            {
                _client!.ReceiveTimeout = 3000;
                byte[] buffer = new byte[2048];
                int read = _stream!.Read(buffer, 0, buffer.Length);
                if (read > 0)
                {
                    string response = Encoding.ASCII.GetString(buffer, 0, read);
                    DebugLog.Write($"[SERVER RESPONSE]\n{response}");
                }
                else
                {
                    DebugLog.Write("[SERVER RESPONSE] No data (Connection closed by server immediately)");
                }
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[SERVER RESPONSE READ ERROR] {ex.Message}");
            }
        }

        private void SendShoutcastV1Headers()
        {
            // Legacy Shoutcast v1 Source
            // 1. Send Password
            DebugLog.Write("[HANDSHAKE] Sending Shoutcast v1 Password...");
            byte[] pass = Encoding.ASCII.GetBytes($"{_config.Password}\r\n");
            _stream?.Write(pass, 0, pass.Length);
            _stream?.Flush();

            // 2. Read Response (Expect OK2)
            if (_stream != null)
            {
                _stream.ReadTimeout = 2000;
                try 
                {
                    byte[] buffer = new byte[1024];
                    int read = _stream.Read(buffer, 0, buffer.Length); 
                    if (read > 0)
                    {
                        string response = Encoding.ASCII.GetString(buffer, 0, read);
                        DebugLog.Write($"[SERVER RESPONSE v1] {response}");
                    }
                } 
                catch (Exception ex)
                {
                    DebugLog.Write($"[SERVER RESPONSE v1 READ WARNING] {ex.Message}");
                }

                // 3. Send ICY Headers (Required by SCv2 in Legacy Mode)
                var sb = new StringBuilder();
                sb.Append($"icy-name:{_stationName}\r\n");
                sb.Append($"icy-url:http://{_config.Host}\r\n");
                sb.Append($"icy-genre:Radio\r\n");
                sb.Append($"icy-public:0\r\n");
                sb.Append($"icy-br:{_config.Bitrate}\r\n");
                sb.Append("\r\n"); // End of headers

                string headerStr = sb.ToString();
                DebugLog.Write($"[HANDSHAKE] Sending ICY Headers:\n{headerStr}");
                
                byte[] headers = Encoding.ASCII.GetBytes(headerStr);
                _stream?.Write(headers, 0, headers.Length);
                _stream?.Flush();
            }
        }

        public void PushData(byte[] data, int length)
        {
            // Allow buffering even if not connected yet (Pre-roll)
            
            byte[] chunk = new byte[length];
            Array.Copy(data, chunk, length);
            _bufferQueue.Enqueue(chunk);

            // Safety cap: Drop old packets if queue gets too large (e.g. connection hangs)
            while (_bufferQueue.Count > 1000)
            {
                _bufferQueue.TryDequeue(out _);
            }
        }

        private void SendLoop()
        {
            _stream?.Flush();
            
            while (_isConnected && _client != null && _client.Connected)
            {
                if (_bufferQueue.TryDequeue(out var chunk))
                {
                    try
                    {
                        _stream?.Write(chunk, 0, chunk.Length);
                    }
                    catch (Exception ex)
                    {
                        DebugLog.Write($"Streaming Write Error: {ex.Message}");
                        HandleDisconnect($"Write Error: {ex.Message}");
                    }
                }
                else
                {
                    Thread.Sleep(10);
                }
            }
            
            if (_isConnected) HandleDisconnect("Socket closed unexpectedly");
        }

        private void HandleDisconnect(string reason)
        {
            if (!_isConnected) return;
            _isConnected = false;
            DebugLog.Write($"DISCONNECTED: {reason}");
            
            try { _stream?.Close(); } catch { }
            try { _client?.Close(); } catch { }
            
            Disconnected?.Invoke(reason);
        }

        public void Disconnect()
        {
            if (!_isConnected) return;
            _isConnected = false;
            
            try { _stream?.Close(); } catch { }
            try { _client?.Close(); } catch { }
            
            // Clear buffer
            while (_bufferQueue.TryDequeue(out _)) { }
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}