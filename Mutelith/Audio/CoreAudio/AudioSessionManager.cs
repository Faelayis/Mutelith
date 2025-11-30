using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Mutelith.Audio.CoreAudio {
	public class AudioSessionManager : IDisposable {
		private IAudioSessionManager2 _manager;

		internal AudioSessionManager(IAudioSessionManager2 manager) {
			_manager = manager;
		}

		public IEnumerable<AudioSession> GetSessions() {
			if (_manager == null) {
				yield break;
			}

			IAudioSessionEnumerator enumerator = null;
			try {
				int hr = _manager.GetSessionEnumerator(out enumerator);
				if (hr != 0 || enumerator == null) {
					yield break;
				}

				int count;
				enumerator.GetCount(out count);

				for (int i = 0; i < count; i++) {
					IAudioSessionControl sessionControl;
					enumerator.GetSession(i, out sessionControl);

					if (sessionControl != null) {
						yield return new AudioSession(sessionControl);
					}
				}
			} finally {
				if (enumerator != null) {
					Marshal.ReleaseComObject(enumerator);
				}
			}
		}

		public void Dispose() {
			if (_manager != null) {
				Marshal.ReleaseComObject(_manager);
				_manager = null;
			}
		}
	}
}
