using System;
using System.Runtime.InteropServices;

namespace VolumeChangeDetector
{
    // IMMDeviceEnumerator interface (to enumerate devices)
    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IMMDeviceCollection ppDevices);
        void GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
        void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
        void RegisterEndpointNotificationCallback(IMMNotificationClient pClient);
        void UnregisterEndpointNotificationCallback(IMMNotificationClient pClient);
    }

    // IMMDevice interface
    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice
    {
        void Activate([In, MarshalAs(UnmanagedType.LPStruct)] Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    // IMMDeviceCollection interface (not used in this specific example but declared for completeness)
    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-C0C00BB8729E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceCollection
    {
        void GetCount(out uint pcDevices);
        void Item(uint nDevice, out IMMDevice ppDevice);
    }

    // IMMNotificationClient interface (to receive device notifications)
    [ComImport]
    [Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMNotificationClient
    {
        void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, int dwNewState);
        void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);
        void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);
        void OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string pwstrDefaultDeviceId);
        void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, PropertyKey key);
    }

    // Define MMDeviceEnumeratorComObject for instantiating the enumerator
    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    [ClassInterface(ClassInterfaceType.None)]
    public class MMDeviceEnumeratorComObject
    {
    }

    // IAudioEndpointVolume interface to monitor volume changes
    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(IAudioEndpointVolumeCallback pNotify);
        void UnregisterControlChangeNotify(IAudioEndpointVolumeCallback pNotify);
        void GetMasterVolumeLevelScalar(out float pfLevelNorm);
        void GetMute(out bool pbMute);
    }

    // IAudioEndpointVolumeCallback interface to receive volume change notifications
    [ComImport]
    [Guid("657804FA-D6AD-4496-8A60-352752AF4F89")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioEndpointVolumeCallback
    {
        void OnNotify(IntPtr pNotifyData);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AUDIO_VOLUME_NOTIFICATION_DATA
    {
        public Guid guidEventContext;
        public bool bMuted;
        public float fMasterVolume;
        public uint nChannels;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public float[] afChannelVolumes;
    }


    // Main class to handle audio notifications
    public class AudioNotificationClient : IMMNotificationClient, IAudioEndpointVolumeCallback
    {
        private IMMDeviceEnumerator _deviceEnumerator;
        private IAudioEndpointVolume _audioEndpointVolume;
        private IntPtr _notifyPtr;

        public AudioNotificationClient()
        {
            // Create the device enumerator using CoCreateInstance
            _deviceEnumerator = (IMMDeviceEnumerator)Activator.CreateInstance(typeof(MMDeviceEnumeratorComObject));
        }

        // Register for volume notifications
        public void RegisterForVolumeNotifications()
        {
            IMMDevice defaultDevice;
            _deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out defaultDevice);
            defaultDevice.Activate(typeof(IAudioEndpointVolume).GUID, 0, IntPtr.Zero, out object endpointVolumeObj);
            _audioEndpointVolume = (IAudioEndpointVolume)endpointVolumeObj;

            _notifyPtr = Marshal.GetComInterfaceForObject(this, typeof(IAudioEndpointVolumeCallback));
            _audioEndpointVolume.RegisterControlChangeNotify(this);
        }

        // Unregister volume notifications
        public void Unregister()
        {
            if (_audioEndpointVolume != null && _notifyPtr != IntPtr.Zero)
            {
                _audioEndpointVolume.UnregisterControlChangeNotify(this);
                Marshal.Release(_notifyPtr);
            }
        }

        // This is called when the volume changes
        public void OnNotify(IntPtr pNotifyData)
        {
            // Marshal the notification data structure from the provided pointer
            var notifyData = Marshal.PtrToStructure<AUDIO_VOLUME_NOTIFICATION_DATA>(pNotifyData);

            // Extract the relevant fields (muted state, master volume, etc.)
            bool isMuted = notifyData.bMuted;
            float volumeScalar = notifyData.fMasterVolume;  // This is already in the 0.0 to 1.0 range

            // Convert the scalar to percentage
            float volumePercentage = volumeScalar * 100;

            // Display whether the system is muted and the current volume percentage
            if (isMuted)
            {
                Console.WriteLine("Volume is muted.");
            }
            else
            {
                Console.WriteLine($"Volume changed: {volumePercentage}%");
            }
        }

        // IMMNotificationClient methods (empty implementations)
        public void OnDeviceStateChanged(string deviceId, int newState) { }
        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId) { }
        public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string pwstrDefaultDeviceId) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }

    // Data flow enum (input/output)
    public enum EDataFlow
    {
        eRender,
        eCapture,
        eAll
    }

    // Role enum (multimedia, communications, etc.)
    public enum ERole
    {
        eConsole,
        eMultimedia,
        eCommunications
    }

    // PropertyKey struct for OnPropertyValueChanged
    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;
    }
}
