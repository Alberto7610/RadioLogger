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

                bool isIcecast = _config.ServerType.Contains("Icecast", StringComparison.OrdinalIgnoreCase);
                bool isV1 = _config.ServerType.Contains("v1") || _config.ServerType == "Shoutcast";

                int sampleRate = _config.SampleRate > 0 ? _config.SampleRate : 44100;
                int channels = _config.Channels == 1 ? 1 : 2;
                string channelMode = channels == 1 ? "-m m" : "-m s";

                _log.Information("Iniciando stream ({Station}) → {Host}:{Port} [{ServerType}] {Bitrate}kbps {SampleRate}Hz {Channels}",
                    _stationName, _config.Host, _config.Port, _config.ServerType,
                    _config.Bitrate, sampleRate, channels == 1 ? "Mono" : "Stereo");

                // 1. Encoder LAME con parámetros configurables
                string lamePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lame.exe");
                string command = $"\"{lamePath}\" -r -s {sampleRate} -b {_config.Bitrate} {channelMode} -";

                _encodeHandle = BassEnc.EncodeStart(_sourceChannel, command, EncodeFlags.NoHeader | EncodeFlags.AutoFree, null, IntPtr.Zero);

                if (_encodeHandle == 0)
                {
                    _log.Error("Encoder failed: {Error}", Bass.LastError);
                    return false;
                }

                // 2. Construir URL y contraseña según tipo de servidor
                string url;
                string pass;

                if (isIcecast)
                {
                    // Icecast: URL incluye mount point, password incluye username
                    var mount = _config.MountPoint.StartsWith("/") ? _config.MountPoint : "/" + _config.MountPoint;
                    url = $"{_config.Host}:{_config.Port}{mount}";
                    var username = string.IsNullOrEmpty(_config.Username) ? "source" : _config.Username;
                    pass = $"{username}:{_config.Password}";

                    _log.Debug("Icecast: URL={Url}, User={User}, Mount={Mount}", url, username, mount);
                }
                else
                {
                    // Shoutcast v1/v2
                    url = $"{_config.Host}:{_config.Port}";
                    pass = _config.Password;

                    // Shoutcast v2: SID support desde mount point
                    if (!isV1 && !string.IsNullOrEmpty(_config.MountPoint) && _config.MountPoint.Contains('/'))
                    {
                        var parts = _config.MountPoint.Split('/');
                        var sid = parts[^1]; // último segmento
                        if (int.TryParse(sid, out _))
                        {
                            pass = $"{_config.Password},{sid}";
                            _log.Debug("Shoutcast v2 SID: ****,SID={Sid}", sid);
                        }
                    }
                }

                // 3. CastInit — genre y metadata opcionales
                string contentType = "audio/mpeg";
                string? genre = string.IsNullOrWhiteSpace(_config.Genre) ? null : _config.Genre;

                bool success = BassEnc.CastInit(
                    _encodeHandle, url, pass, contentType,
                    _stationName,   // name
                    null,           // url (website)
                    genre,          // genre
                    null,           // desc
                    null,           // headers
                    _config.Bitrate,
                    isV1);          // public (Shoutcast v1 uses this as "is public" flag)

                if (!success)
                {
                    _log.Warning("Stream fallido ({Station}): CastInit Error {Error}", _stationName, Bass.LastError);
                    Stop();
                    return false;
                }

                _log.Information("Stream conectado ({Station}): {Url}", _stationName, url);
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
