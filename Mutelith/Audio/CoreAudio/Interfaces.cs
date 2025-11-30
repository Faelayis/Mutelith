using System;
using System.Runtime.InteropServices;

namespace Mutelith.Audio.CoreAudio {
	[Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IMMDevice {
		[PreserveSig]
		int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

		[PreserveSig]
		int OpenPropertyStore(int stgmAccess, out IPropertyStore ppProperties);

		[PreserveSig]
		int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);

		[PreserveSig]
		int GetState(out DeviceState pdwState);
	}

	[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IMMDeviceCollection {
		[PreserveSig]
		int GetCount(out int pcDevices);

		[PreserveSig]
		int Item(int nDevice, out IMMDevice ppDevice);
	}

	[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IMMDeviceEnumerator {
		[PreserveSig]
		int EnumAudioEndpoints(DataFlow dataFlow, DeviceState dwStateMask, out IMMDeviceCollection ppDevices);

		[PreserveSig]
		int GetDefaultAudioEndpoint(DataFlow dataFlow, Role role, out IMMDevice ppEndpoint);

		[PreserveSig]
		int GetDevice(string pwstrId, out IMMDevice ppDevice);

		[PreserveSig]
		int RegisterEndpointNotificationCallback(IntPtr pClient);

		[PreserveSig]
		int UnregisterEndpointNotificationCallback(IntPtr pClient);
	}

	[Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IPropertyStore {
		[PreserveSig]
		int GetCount(out int cProps);

		[PreserveSig]
		int GetAt(int iProp, out PropertyKey pkey);

		[PreserveSig]
		int GetValue(ref PropertyKey key, out PropVariant pv);

		[PreserveSig]
		int SetValue(ref PropertyKey key, ref PropVariant propvar);

		[PreserveSig]
		int Commit();
	}

	[Guid("BFA971F1-4D5E-40BB-935E-967039BFBEE4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IAudioSessionManager {
		[PreserveSig]
		int GetAudioSessionControl(ref Guid AudioSessionGuid, int StreamFlags, out IAudioSessionControl SessionControl);

		[PreserveSig]
		int GetSimpleAudioVolume(ref Guid AudioSessionGuid, int StreamFlags, out ISimpleAudioVolume AudioVolume);
	}

	[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IAudioSessionManager2 {
		[PreserveSig]
		int GetAudioSessionControl(ref Guid AudioSessionGuid, int StreamFlags, out IAudioSessionControl SessionControl);

		[PreserveSig]
		int GetSimpleAudioVolume(ref Guid AudioSessionGuid, int StreamFlags, out ISimpleAudioVolume AudioVolume);

		[PreserveSig]
		int GetSessionEnumerator(out IAudioSessionEnumerator SessionEnum);

		[PreserveSig]
		int RegisterSessionNotification(IntPtr SessionNotification);

		[PreserveSig]
		int UnregisterSessionNotification(IntPtr SessionNotification);

		[PreserveSig]
		int RegisterDuckNotification(string sessionID, IntPtr duckNotification);

		[PreserveSig]
		int UnregisterDuckNotification(IntPtr duckNotification);
	}

	[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IAudioSessionEnumerator {
		[PreserveSig]
		int GetCount(out int SessionCount);

		[PreserveSig]
		int GetSession(int SessionIndex, out IAudioSessionControl Session);
	}

	[Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IAudioSessionControl {
		[PreserveSig]
		int GetState(out AudioSessionState pRetVal);

		[PreserveSig]
		int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

		[PreserveSig]
		int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);

		[PreserveSig]
		int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

		[PreserveSig]
		int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);

		[PreserveSig]
		int GetGroupingParam(out Guid pRetVal);

		[PreserveSig]
		int SetGroupingParam(ref Guid Override, ref Guid EventContext);

		[PreserveSig]
		int RegisterAudioSessionNotification(IntPtr NewNotifications);

		[PreserveSig]
		int UnregisterAudioSessionNotification(IntPtr NewNotifications);
	}

	[Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IAudioSessionControl2 {
		[PreserveSig]
		int GetState(out AudioSessionState pRetVal);

		[PreserveSig]
		int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

		[PreserveSig]
		int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);

		[PreserveSig]
		int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

		[PreserveSig]
		int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);

		[PreserveSig]
		int GetGroupingParam(out Guid pRetVal);

		[PreserveSig]
		int SetGroupingParam(ref Guid Override, ref Guid EventContext);

		[PreserveSig]
		int RegisterAudioSessionNotification(IntPtr NewNotifications);

		[PreserveSig]
		int UnregisterAudioSessionNotification(IntPtr NewNotifications);

		[PreserveSig]
		int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

		[PreserveSig]
		int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

		[PreserveSig]
		int GetProcessId(out uint pRetVal);

		[PreserveSig]
		int IsSystemSoundsSession();

		[PreserveSig]
		int SetDuckingPreference(bool optOut);
	}

	[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface ISimpleAudioVolume {
		[PreserveSig]
		int SetMasterVolume(float fLevel, ref Guid EventContext);

		[PreserveSig]
		int GetMasterVolume(out float pfLevel);

		[PreserveSig]
		int SetMute(bool bMute, ref Guid EventContext);

		[PreserveSig]
		int GetMute(out bool pbMute);
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct PropertyKey {
		public Guid fmtid;
		public int pid;
	}

	[StructLayout(LayoutKind.Explicit)]
	internal struct PropVariant {
		[FieldOffset(0)] public short vt;
		[FieldOffset(8)] public IntPtr pwszVal;

		public static PropVariant FromString(string value) {
			var pv = new PropVariant {
				vt = 31,
				pwszVal = Marshal.StringToCoTaskMemUni(value)
			};
			return pv;
		}

		public void Clear() {
			if (vt == 31 && pwszVal != IntPtr.Zero) {
				Marshal.FreeCoTaskMem(pwszVal);
				pwszVal = IntPtr.Zero;
			}
		}
	}

	internal static class PropertyKeys {
		public static readonly PropertyKey PKEY_Device_FriendlyName = new PropertyKey {
			fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
			pid = 14
		};
	}
}