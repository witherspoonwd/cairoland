using System;
using System.Runtime.InteropServices;

namespace CairoDesktop.Interop
{
    /// <summary>
    /// Just enough of the Core Audio API to read and set the default output device's master volume.
    /// </summary>
    public class CoreAudio
    {
        private const int CLSCTX_ALL = 0x17;

        private enum EDataFlow
        {
            eRender = 0
        }

        private enum ERole
        {
            eMultimedia = 1
        }

        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumerator
        {
        }

        [ComImport]
        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(EDataFlow dataFlow, int dwStateMask, out IntPtr ppDevices);

            int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
        }

        [ComImport]
        [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        }

        [ComImport]
        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr pNotify);

            int UnregisterControlChangeNotify(IntPtr pNotify);

            int GetChannelCount(out uint pnChannelCount);

            int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);

            int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);

            int GetMasterVolumeLevel(out float pfLevelDB);

            int GetMasterVolumeLevelScalar(out float pfLevel);
        }

        private static IAudioEndpointVolume GetEndpointVolume()
        {
            IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            IMMDevice device = null;

            try
            {
                if (enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out device) != 0 || device == null)
                {
                    return null;
                }

                Guid iid = typeof(IAudioEndpointVolume).GUID;
                if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out object endpointVolume) != 0)
                {
                    return null;
                }

                return endpointVolume as IAudioEndpointVolume;
            }
            finally
            {
                if (device != null)
                {
                    Marshal.ReleaseComObject(device);
                }

                Marshal.ReleaseComObject(enumerator);
            }
        }

        /// <summary>
        /// Changes the default output device's master volume by <paramref name="delta"/> (0..1 scale), clamped to 0..1.
        /// </summary>
        /// <returns>False if there is no output device.</returns>
        public static bool AdjustMasterVolume(float delta)
        {
            IAudioEndpointVolume endpointVolume = GetEndpointVolume();
            if (endpointVolume == null)
            {
                return false;
            }

            try
            {
                if (endpointVolume.GetMasterVolumeLevelScalar(out float level) != 0)
                {
                    return false;
                }

                Guid eventContext = Guid.Empty;
                float newLevel = Math.Max(0f, Math.Min(1f, level + delta));
                return endpointVolume.SetMasterVolumeLevelScalar(newLevel, ref eventContext) == 0;
            }
            finally
            {
                Marshal.ReleaseComObject(endpointVolume);
            }
        }
    }
}
