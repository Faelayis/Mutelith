namespace Mutelith {
	public static class AudioManagerFactory {
		public static IAudioManager CreateManager(AudioDeviceType deviceType) {
			switch (deviceType) {
				case AudioDeviceType.SteelSeriesSonar:
					Logger.Info("Using SonarManager for audio management");
					return new SonarManager();

				case AudioDeviceType.FxSound:
					Logger.Info("Using FxSoundManager for audio management");
					return new FxSoundManager();

				case AudioDeviceType.None:
				default:
					Logger.Info("No compatible audio device detected - waiting for device change...");
					return null;
			}
		}
	}
}