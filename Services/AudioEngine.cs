using ManagedBass;
using RadioLogger.Models;
using System.Collections.Generic;
using System.Linq;
using System;

namespace RadioLogger.Services
{
    public class AudioEngine
    {
        private readonly ConfigManager _configManager;
        private readonly Dictionary<int, AudioChannel> _activeChannels = new Dictionary<int, AudioChannel>();

        public AudioEngine(ConfigManager configManager)
        {
            _configManager = configManager;
            InitializeBass();
        }

        private void InitializeBass()
        {
            // Verificación de versión de la DLL nativa (equivalente a tu snippet)
            // En ManagedBass, Bass.Version devuelve la versión de la DLL cargada.
            var loadedVersion = Bass.Version;
            var expectedVersion = new System.Version(2, 4); // BASS 2.4.x

            if (loadedVersion.Major < expectedVersion.Major || (loadedVersion.Major == expectedVersion.Major && loadedVersion.Minor < expectedVersion.Minor))
            {
                System.Windows.MessageBox.Show($"¡No se ha podido encontrar el archivo bass.dll correcto! \nVersión detectada: {loadedVersion}");
                return;
            }

            // Set Config para mejor compatibilidad y estabilidad 24/7
            Bass.Configure(Configuration.UpdatePeriod, 50);
        }

        public List<AudioDevice> GetInputDevices()
        {
            var devices = new List<AudioDevice>();
            // Start at 1 to skip the OS "Default" alias (Index 0). 
            // We want to bind to specific hardware IDs.
            int i = 1; 
            while (Bass.RecordGetDeviceInfo(i, out var info))
            {
                // Filter: Must be Enabled AND NOT a Loopback (Speakers/What U Hear)
                if (info.IsEnabled && !info.IsLoopback)
                {
                    devices.Add(new AudioDevice
                    {
                        Id = i,
                        Name = info.Name,
                        Driver = info.Driver,
                        IsInput = true,
                        IsEnabled = info.IsEnabled
                    });
                }
                i++;
            }
            return devices;
        }

        public AudioChannel? StartRecording(AudioDevice device, string stationName)
        {
            if (_activeChannels.ContainsKey(device.Id))
            {
                var existing = _activeChannels[device.Id];
                if (existing.StationName != stationName)
                {
                    existing.StationName = stationName;
                    // Note: The file name will update on the next rotation or if we force it.
                    // For now, updating the property is enough for the next rotation.
                }
                return existing;
            }

            var channel = new AudioChannel(device, stationName, _configManager.CurrentSettings);
            if (channel.Initialize())
            {
                channel.Start();
                _activeChannels[device.Id] = channel;
                return channel;
            }
            return null;
        }

        public void StopRecording(AudioDevice device)
        {
            if (_activeChannels.ContainsKey(device.Id))
            {
                var channel = _activeChannels[device.Id];
                channel.Stop();
                channel.Dispose();
                _activeChannels.Remove(device.Id);
            }
        }
        
        public void StartStreaming(AudioDevice device, StreamingConfig config)
        {
            if (_activeChannels.ContainsKey(device.Id))
            {
                _activeChannels[device.Id].StartStreaming(config);
            }
        }

        public void StopStreaming(AudioDevice device)
        {
            if (_activeChannels.ContainsKey(device.Id))
            {
                _activeChannels[device.Id].StopStreaming();
            }
        }
        
        public (bool success, string message) TestConnection(StreamingConfig config)
        {
            var client = new StreamingClient(config, "TEST_CONN");
            if (client.Connect())
            {
                // Wait for Handshake response
                System.Threading.Thread.Sleep(2000); 
                
                // If we are still connected/sending, it's likely good. 
                // If server rejected (Bad Pass), it usually closes the socket.
                // Note: This is a heuristic.
                client.Disconnect();
                return (true, "Conexión establecida (Handshake enviado). Verifique logs si falla.");
            }
            else
            {
                return (false, "No se pudo conectar al servidor (Puerto cerrado o IP incorrecta).");
            }
        }

        public void SetChannelGain(AudioDevice device, float gain)
        {
            if (_activeChannels.ContainsKey(device.Id))
            {
                _activeChannels[device.Id].SetGain(gain);
            }
        }

        // State Queries for ViewModel Sync
        public bool IsDeviceRecording(int deviceId)
        {
            return _activeChannels.ContainsKey(deviceId) && _activeChannels[deviceId].IsRecording;
        }

        public bool IsDeviceStreaming(int deviceId)
        {
            return _activeChannels.ContainsKey(deviceId) && _activeChannels[deviceId].IsStreaming;
        }

        public bool IsDeviceReconnecting(int deviceId)
        {
            return _activeChannels.ContainsKey(deviceId) && _activeChannels[deviceId].IsReconnecting;
        }

        public DateTime? GetDeviceStartTime(int deviceId)
        {
            if (_activeChannels.ContainsKey(deviceId))
                return _activeChannels[deviceId].StartTime;
            return null;
        }
    }
}