namespace Mutelith {
	public class SonarManager : ViewerEcho {
		protected override string TargetDeviceName => SonarConfig.DEVICE_SONAR_MICROPHONE;
		protected override string DevicePrefix => SonarConfig.DEVICE_SONAR_PREFIX;
	}
}