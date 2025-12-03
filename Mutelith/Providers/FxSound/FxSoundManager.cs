namespace Mutelith {
	public class FxSoundManager : ViewerEcho {
		protected override string TargetDeviceName => FxSoundConfig.DEVICE_FXSOUND_SPEAKERS;
		protected override string DevicePrefix => FxSoundConfig.DEVICE_FXSOUND_PREFIX;
	}
}