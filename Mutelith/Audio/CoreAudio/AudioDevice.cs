using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Mutelith.Audio.CoreAudio {
	public class AudioDevice : IDisposable {
		private IMMDevice _device;
		private string _friendlyName;
		private DeviceState _state;
		internal IMMDevice Device => _device;

		internal AudioDevice(IMMDevice device) {
			_device = device;
			LoadDeviceInfo();
		}

		public string FriendlyName {
			get {
				if (_friendlyName == null) {
					LoadDeviceInfo();
				}
				return _friendlyName ?? "";
			}
		}

		public DeviceState State {
			get {
				if (_device != null) {
					DeviceState state;
					_device.GetState(out state);
					_state = state;
				}
				return _state;
			}
		}

		private void LoadDeviceInfo() {
			if (_device == null) {
				return;
			}

			IPropertyStore propertyStore = null;
			try {
				_device.OpenPropertyStore(0, out propertyStore);
				if (propertyStore != null) {
					var key = PropertyKeys.PKEY_Device_FriendlyName;
					PropVariant variant;
					propertyStore.GetValue(ref key, out variant);

					if (variant.vt == 31 && variant.pwszVal != IntPtr.Zero) {
						_friendlyName = Marshal.PtrToStringUni(variant.pwszVal);
						variant.Clear();
					}
				}
			} catch {
			} finally {
				if (propertyStore != null) {
					Marshal.ReleaseComObject(propertyStore);
				}
			}

			try {
				_device.GetState(out _state);
			} catch {
				_state = DeviceState.NotPresent;
			}
		}

		public AudioSessionManager GetAudioSessionManager() {
			if (_device == null) {
				return null;
			}

			try {
				var iid = CoreAudioConstants.IID_IAudioSessionManager2;
				object obj;
				int hr = _device.Activate(ref iid, 0, IntPtr.Zero, out obj);

				if (hr == 0 && obj != null) {
					return new AudioSessionManager((IAudioSessionManager2)obj);
				}
			} catch {
			}

			return null;
		}

		public void Dispose() {
			if (_device != null) {
				Marshal.ReleaseComObject(_device);
				_device = null;
			}
		}
	}
}
