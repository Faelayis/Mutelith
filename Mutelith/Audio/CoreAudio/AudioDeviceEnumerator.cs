using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Mutelith.Audio.CoreAudio {
	public class AudioDeviceEnumerator : IDisposable {
		private IMMDeviceEnumerator _enumerator;

		public AudioDeviceEnumerator() {
			var enumeratorType = Type.GetTypeFromCLSID(CoreAudioConstants.CLSID_MMDeviceEnumerator);
			_enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType);
		}

		public IEnumerable<AudioDevice> EnumerateAudioEndPoints(DataFlow dataFlow, DeviceState stateMask) {
			if (_enumerator == null) {
				yield break;
			}

			IMMDeviceCollection deviceCollection;
			int hr = _enumerator.EnumAudioEndpoints(dataFlow, stateMask, out deviceCollection);
			if (hr != 0) {
				yield break;
			}

			try {
				int count;
				deviceCollection.GetCount(out count);

				for (int i = 0; i < count; i++) {
					IMMDevice device;
					deviceCollection.Item(i, out device);

					if (device != null) {
						yield return new AudioDevice(device);
					}
				}
			} finally {
				if (deviceCollection != null) {
					Marshal.ReleaseComObject(deviceCollection);
				}
			}
		}
		public AudioDevice GetDefaultAudioEndpoint(DataFlow dataFlow, Role role) {
			if (_enumerator == null) {
				return null;
			}

			try {
				IMMDevice device;
				int hr = _enumerator.GetDefaultAudioEndpoint(dataFlow, role, out device);
				if (hr == 0 && device != null) {
					return new AudioDevice(device);
				}
			} catch {
			}

			return null;
		}

		public void Dispose() {
			if (_enumerator != null) {
				Marshal.ReleaseComObject(_enumerator);
				_enumerator = null;
			}
		}
	}
}