using System;
using System.Runtime.InteropServices;

namespace RadioLogger.Helpers
{
    public static class SystemAudioHelper
    {
        public static float GetMasterVolume()
        {
            try
            {
                var deviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out IMMDevice speakers);
                speakers.Activate(typeof(IAudioEndpointVolume).GUID, 0, IntPtr.Zero, out object o);
                var vol = (IAudioEndpointVolume)o;
                
                vol.GetMasterVolumeLevelScalar(out float level);
                return level * 100; // Return 0-100
            }
            catch
            {
                return 50; // Fallback
            }
        }

        public static void SetMasterVolume(float level)
        {
            try
            {
                // Level is 0-100
                float scalar = level / 100.0f;
                
                var deviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out IMMDevice speakers);
                speakers.Activate(typeof(IAudioEndpointVolume).GUID, 0, IntPtr.Zero, out object o);
                var vol = (IAudioEndpointVolume)o;
                
                vol.SetMasterVolumeLevelScalar(scalar, Guid.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting master volume: {ex.Message}");
            }
        }

        #region COM Interfaces

        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumerator
        {
        }

        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int NotImpl1();
            [PreserveSig]
            int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
            // Other methods omitted
        }

        [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig]
            int Activate([MarshalAs(UnmanagedType.LPStruct)] Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
            // Other methods omitted
        }

        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr pNotify);
            int UnregisterControlChangeNotify(IntPtr pNotify);
            int GetChannelCount(out int pnChannelCount);
            [PreserveSig]
            int SetMasterVolumeLevel(float fLevelDB, Guid pguidEventContext);
            [PreserveSig]
            int SetMasterVolumeLevelScalar(float fLevel, Guid pguidEventContext);
            [PreserveSig]
            int GetMasterVolumeLevel(out float pfLevelDB);
            [PreserveSig]
            int GetMasterVolumeLevelScalar(out float pfLevel);
            // Other methods omitted
        }

        private enum EDataFlow
        {
            eRender,
            eCapture,
            eAll,
            EDataFlow_enum_count
        }

        private enum ERole
        {
            eConsole,
            eMultimedia,
            eCommunications,
            ERole_enum_count
        }

        #endregion
    }
}
