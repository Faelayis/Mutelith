using System;

namespace Mutelith {
	public interface IAudioManager : IDisposable {
		void InitializeAndSaveConfigs();
		void ApplyMuteSettings();
		void RestoreSettings();
	}
}