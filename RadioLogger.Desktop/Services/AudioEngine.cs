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
                // Must be Enabled. We removed the Loopback filter because some real inputs
                // are sometimes flagged as loopbacks by certain drivers.
                if (info.IsEnabled)
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

        public AudioChannel? StartRecording(AudioDevice device, string stationName, bool recordToFile = true)
        {
            if (_activeChannels.ContainsKey(device.Id))
            {
                var existing = _activeChannels[device.Id];
                if (existing.StationName != stationName)
                {
                    existing.StationName = stationName;
                }
                return existing;
            }

            var channel = new AudioChannel(device, stationName, _configManager.CurrentSettings);
            channel.RecordToFile = recordToFile;
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
                // Force pull latest from ConfigManager just in case
                if (_configManager.CurrentSettings.DeviceStreamingConfigs.TryGetValue(device.Name, out var latestConfig))
                {
                    _activeChannels[device.Id].StartStreaming(latestConfig);
                }
                else
                {
                    _activeChannels[device.Id].StartStreaming(config);
                }
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
            try 
            {
                // To test connection, we need a dummy recording handle
                Bass.RecordInit(1);
                int dummyHandle = Bass.RecordStart(44100, 2, BassFlags.RecordPause, null, IntPtr.Zero);
                
                using var client = new StreamingClient(config, "TEST_CONN", dummyHandle);
                if (client.Connect())
                {
                    System.Threading.Thread.Sleep(3000); 
                    Bass.ChannelStop(dummyHandle);
                    return (true, "Conexión Exitosa (Servidor aceptó Handshake)");
                }
                else
                {
                    Bass.ChannelStop(dummyHandle);
                    return (false, "Error: El servidor rechazó la conexión o puerto cerrado.");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Error de Red: {ex.Message}");
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

        public string? GetDeviceStreamUrl(int deviceId)
        {
            if (_activeChannels.ContainsKey(deviceId))
                return _activeChannels[deviceId].StreamUrl;
            return null;
        }

        public DateTime? GetDeviceStartTime(int deviceId)
        {
            if (_activeChannels.ContainsKey(deviceId))
                return _activeChannels[deviceId].StartTime;
            return null;
        }

        public void UpdateAllSettings(AppSettings settings)
        {
            foreach (var channel in _activeChannels.Values)
            {
                channel.UpdateSettings(settings);
            }
        }
    }
}