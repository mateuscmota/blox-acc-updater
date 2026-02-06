using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RBX_Alt_Manager.Classes
{
    public static class AudioHelper
    {
        // COM GUIDs
        private static readonly Guid CLSID_MMDeviceEnumerator = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
        private static readonly Guid IID_IAudioSessionManager2 = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

        // EDataFlow
        private const int eRender = 0;
        // ERole
        private const int eMultimedia = 1;

        public static void MuteAllRobloxProcesses()
        {
            try
            {
                var enumeratorType = Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator);
                if (enumeratorType == null) return;

                var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType);
                enumerator.GetDefaultAudioEndpoint(eRender, eMultimedia, out IMMDevice device);
                if (device == null) return;

                device.Activate(IID_IAudioSessionManager2, 0, IntPtr.Zero, out object obj);
                var sessionManager = (IAudioSessionManager2)obj;
                if (sessionManager == null) return;

                sessionManager.GetSessionEnumerator(out IAudioSessionEnumerator sessionEnumerator);
                if (sessionEnumerator == null) return;

                sessionEnumerator.GetCount(out int count);

                for (int i = 0; i < count; i++)
                {
                    sessionEnumerator.GetSession(i, out IAudioSessionControl sessionControl);
                    if (sessionControl == null) continue;

                    var sessionControl2 = sessionControl as IAudioSessionControl2;
                    if (sessionControl2 == null) continue;

                    sessionControl2.GetProcessId(out uint pid);
                    if (pid == 0) continue;

                    try
                    {
                        using (var process = Process.GetProcessById((int)pid))
                        {
                            if (process.ProcessName.IndexOf("RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                var volume = sessionControl as ISimpleAudioVolume;
                                if (volume != null)
                                {
                                    volume.SetMute(true, Guid.Empty);
                                }
                            }
                        }
                    }
                    catch { }
                }

                Marshal.ReleaseComObject(sessionEnumerator);
                Marshal.ReleaseComObject(sessionManager);
                Marshal.ReleaseComObject(device);
                Marshal.ReleaseComObject(enumerator);
            }
            catch { }
        }

        #region COM Interfaces

        [ComImport]
        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        }

        [ComImport]
        [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            int Activate([MarshalAs(UnmanagedType.LPStruct)] Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport]
        [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionManager2
        {
            int GetAudioSessionControl(IntPtr audioSessionGuid, int streamFlags, out IntPtr sessionControl);
            int GetSimpleAudioVolume(IntPtr audioSessionGuid, int streamFlags, out IntPtr simpleAudioVolume);
            int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
            // remaining methods not needed
        }

        [ComImport]
        [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionEnumerator
        {
            int GetCount(out int sessionCount);
            int GetSession(int sessionCount, out IAudioSessionControl session);
        }

        [ComImport]
        [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl
        {
            int GetState(out int state);
            int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
            int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, [MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);
            int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
            int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, [MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);
            int GetGroupingParam(out Guid groupingParam);
            int SetGroupingParam([MarshalAs(UnmanagedType.LPStruct)] Guid @override, [MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);
            int RegisterAudioSessionNotification(IntPtr newNotifications);
            int UnregisterAudioSessionNotification(IntPtr newNotifications);
        }

        [ComImport]
        [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl2
        {
            // IAudioSessionControl methods
            int GetState(out int state);
            int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
            int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, [MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);
            int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
            int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, [MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);
            int GetGroupingParam(out Guid groupingParam);
            int SetGroupingParam([MarshalAs(UnmanagedType.LPStruct)] Guid @override, [MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);
            int RegisterAudioSessionNotification(IntPtr newNotifications);
            int UnregisterAudioSessionNotification(IntPtr newNotifications);
            // IAudioSessionControl2 methods
            int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string retVal);
            int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string retVal);
            int GetProcessId(out uint retVal);
            int IsSystemSoundsSession();
            int SetDuckingPreference(bool optOut);
        }

        [ComImport]
        [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISimpleAudioVolume
        {
            int SetMasterVolume(float level, [MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);
            int GetMasterVolume(out float level);
            int SetMute(bool mute, [MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);
            int GetMute(out bool mute);
        }

        #endregion
    }
}
