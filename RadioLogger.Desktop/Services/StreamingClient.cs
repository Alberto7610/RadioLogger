using ManagedBass;
using ManagedBass.Enc;
using RadioLogger.Models;
using System;
using System.IO;

namespace RadioLogger.Services
{
    public class StreamingClient : IDisposable
    {
        private int _encodeHandle;
        private readonly StreamingConfig _config;
        private readonly string _stationName;
        private readonly int _sourceChannel;
        private bool _isDisposed;

        public bool IsConnected => _encodeHandle != 0 && BassEnc.EncodeIsActive(_encodeHandle) == PlaybackState.Playing;

        public StreamingClient(StreamingConfig config, string stationName, int sourceChannel)
        {
            _config = config;
            _stationName = stationName;
            _sourceChannel = sourceChannel;
        }

        public bool Connect()
        {
            try
            {
                // Limpiar cualquier conexión previa antes de empezar para evitar bloqueos
                Stop();

                DebugLog.Write($"[STREAM] Starting Native Cast Engine to {_config.Host}:{_config.Port} (SID Support)");

                // 1. Setup LAME con parámetros profesionales
                string lamePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lame.exe");
                string command = $"\"{lamePath}\" -r -s 44100 -b {_config.Bitrate} -";
                
                _encodeHandle = BassEnc.EncodeStart(_sourceChannel, command, EncodeFlags.NoHeader | EncodeFlags.AutoFree, null, IntPtr.Zero);

                if (_encodeHandle == 0)
                {
                    DebugLog.Write($"[STREAM ERROR] Encoder failed: {Bass.LastError}");
                    return false;
                }

                // 2. Formatear contraseña para soporte de SID (Shoutcast v2/v1 mix)
                // Si el mount point es algo como "/stream/3", extraemos el SID
                string passWithSid = _config.Password;
                if (!string.IsNullOrEmpty(_config.MountPoint) && _config.MountPoint.Contains("/"))
                {
                    var parts = _config.MountPoint.Split('/');
                    var sid = parts[parts.Length - 1];
                    if (int.TryParse(sid, out _))
                    {
                        passWithSid = $"{_config.Password},{sid}";
                        DebugLog.Write($"[STREAM] Using SID formatting for password: {_config.Password},SID={sid}");
                    }
                }

                string url = $"{_config.Host}:{_config.Port}";
                string contentType = "audio/mpeg";
                bool isV1 = _config.ServerType.Contains("v1") || _config.ServerType == "Shoutcast";

                // 3. Iniciar Cast con la contraseña formateada para SID
                bool success = BassEnc.CastInit(_encodeHandle, url, passWithSid, contentType, _stationName, null, null, null, null, _config.Bitrate, isV1);

                if (!success)
                {
                    DebugLog.Write($"[STREAM ERROR] CastInit Failed: {Bass.LastError}");
                    Stop();
                    return false;
                }

                DebugLog.Write("[STREAM SUCCESS] Connected to SID successfully.");
                return true;
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[STREAM EXCEPTION] {ex.Message}");
                return false;
            }
        }

        public void Stop()
        {
            if (_encodeHandle != 0)
            {
                // Detener el Cast y el Encoder inmediatamente
                BassEnc.EncodeStop(_encodeHandle);
                _encodeHandle = 0;
                DebugLog.Write("[STREAM] Native engine stopped and ports released.");
            }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                Stop();
                _isDisposed = true;
            }
        }
    }
}