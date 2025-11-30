using System;
using System.Runtime.InteropServices;

namespace Mutelith.Audio.CoreAudio {
	public class AudioSession : IDisposable {
		private IAudioSessionControl _control;
		private IAudioSessionControl2 _control2;
		private ISimpleAudioVolume _volumeControl;
		private uint? _processId;

		internal AudioSession(IAudioSessionControl control) {
			_control = control;
			_control2 = control as IAudioSessionControl2;
		}

		public uint ProcessId {
			get {
				if (_processId == null && _control2 != null) {
					uint pid;
					int hr = _control2.GetProcessId(out pid);
					if (hr == 0) {
						_processId = pid;
					}
				}
				return _processId ?? 0;
			}
		}

		public float Volume {
			get {
				EnsureVolumeControl();
				if (_volumeControl != null) {
					float volume;
					_volumeControl.GetMasterVolume(out volume);
					return volume;
				}
				return 1.0f;
			}
			set {
				EnsureVolumeControl();
				if (_volumeControl != null) {
					Guid context = Guid.Empty;
					_volumeControl.SetMasterVolume(value, ref context);
				}
			}
		}

		public bool Mute {
			get {
				EnsureVolumeControl();
				if (_volumeControl != null) {
					bool mute;
					_volumeControl.GetMute(out mute);
					return mute;
				}
				return false;
			}
			set {
				EnsureVolumeControl();
				if (_volumeControl != null) {
					Guid context = Guid.Empty;
					_volumeControl.SetMute(value, ref context);
				}
			}
		}

		private void EnsureVolumeControl() {
			if (_volumeControl == null && _control != null) {
				_volumeControl = _control as ISimpleAudioVolume;
			}
		}

		public void Dispose() {
			if (_volumeControl != null && _volumeControl != _control) {
				Marshal.ReleaseComObject(_volumeControl);
				_volumeControl = null;
			}

			if (_control2 != null && _control2 != _control) {
				Marshal.ReleaseComObject(_control2);
				_control2 = null;
			}

			if (_control != null) {
				Marshal.ReleaseComObject(_control);
				_control = null;
			}
		}
	}
}