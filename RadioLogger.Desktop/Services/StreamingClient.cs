using ManagedBass;
using ManagedBass.Enc;
using RadioLogger.Models;
using Serilog;
using System;
using System.IO;

namespace RadioLogger.Services
{
    public class StreamingClient : IDisposable
    {
        private static readonly ILogger _log = AppLog.For<StreamingClient>();

        private int _encodeHandle;
        private readonly StreamingConfig _config;
        private readonly string _stationName;
        private readonly int _sourceChannel;
        private bool _isDisposed;
        private readonly object _lock = new();

        public bool IsConnected
        {
            get
            {
                try
                {
                    return _encodeHandle != 0 && BassEnc.EncodeIsActive(_encodeHandle) == PlaybackState.Playing;
                }
                catch
                {
                    return false;
                }
            }
        }

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
                Stop();

                _log.Debug("Iniciando Cast Engine hacia {Host}:{Port} (SID Support)", _config.Host, _config.Port);

                string lamePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lame.exe");
                string command = $"\"{lamePath}\" -r -s 44100 -b {_config.Bitrate} -";

                _encodeHandle = BassEnc.EncodeStart(_sourceChannel, command, EncodeFlags.NoHeader | EncodeFlags.AutoFree, null, IntPtr.Zero);

                if (_encodeHandle == 0)
                {
                    _log.Error("Encoder failed: {Error}", Bass.LastError);
                    return false;
                }

                string passWithSid = _config.Password;
                if (!string.IsNullOrEmpty(_config.MountPoint) && _config.MountPoint.Contains("/"))
                {
                    var parts = _config.MountPoint.Split('/');
                    var sid = parts[parts.Length - 1];
                    if (int.TryParse(sid, out _))
                    {
                        passWithSid = $"{_config.Password},{sid}";
                        _log.Debug("Usando formato SID: ****,SID={Sid}", sid);
                    }
                }

                string url = $"{_config.Host}:{_config.Port}";
                string contentType = "audio/mpeg";
                bool isV1 = _config.ServerType.Contains("v1") || _config.ServerType == "Shoutcast";

                bool success = BassEnc.CastInit(_encodeHandle, url, passWithSid, contentType, _stationName, null, null, null, null, _config.Bitrate, isV1);

                if (!success)
                {
                    _log.Warning("Stream fallido ({Station}): CastInit Error {Error}", _stationName, Bass.LastError);
                    Stop();
                    return false;
                }

                _log.Information("Stream conectado ({Station}): {Host}:{Port}", _stationName, _config.Host, _config.Port);
                return true;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Excepción en stream ({Station})", _stationName);
                return false;
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (_encodeHandle != 0)
                {
                    try
                    {
                        BassEnc.EncodeStop(_encodeHandle);
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Error deteniendo encoder");
                    }
                    _encodeHandle = 0;
                    _log.Debug("Stream engine detenido, puertos liberados");
                }
            }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                Stop();
            }
        }
    }
}
