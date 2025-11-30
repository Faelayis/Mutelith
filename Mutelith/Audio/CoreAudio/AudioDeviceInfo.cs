using System;
using System.Runtime.InteropServices;

namespace Mutelith.Audio.CoreAudio {
	public class AudioDeviceInfo {
		private IMMDevice _device;
		public string ControllerInfo { get; private set; }

		internal AudioDeviceInfo(IMMDevice device) {
			_device = device;
			LoadDeviceProperties();
		}

		private void LoadDeviceProperties() {
			IPropertyStore propertyStore = null;
			try {
				_device.OpenPropertyStore(0, out propertyStore);
				if (propertyStore != null) {
					ControllerInfo = GetPropertyAtIndex(propertyStore, 64);
				}
			} catch {
			} finally {
				if (propertyStore != null) {
					Marshal.ReleaseComObject(propertyStore);
				}
			}
		}

		private string GetVariantValueAsString(PropVariant variant) {
			try {
				switch (variant.vt) {
					case 31: // VT_LPWSTR
						if (variant.pwszVal != IntPtr.Zero) {
							return Marshal.PtrToStringUni(variant.pwszVal) ?? "empty";
						}
						return "null pointer";
					default:
						return $"Type:{variant.vt}";
				}
			} catch {
				return "error reading value";
			}
		}

		private string GetPropertyAtIndex(IPropertyStore propertyStore, int index) {
			try {
				PropertyKey key;
				propertyStore.GetAt(index, out key);

				PropVariant variant;
				int hr = propertyStore.GetValue(ref key, out variant);
				if (hr == 0) {
					string value = GetVariantValueAsString(variant);
					variant.Clear();
					if (!string.IsNullOrEmpty(value) && value != "Type:0") {
						return value;
					}
				}
			} catch {
			}
			return "";
		}
	}
}
