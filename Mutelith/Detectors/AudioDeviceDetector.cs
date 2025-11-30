using Mutelith.Audio.CoreAudio;
using System;

namespace Mutelith {
	public enum AudioDeviceType {
		None,
		SteelSeriesSonar,
		FxSound
	}

	public static class AudioDeviceDetector {
		public static AudioDeviceType DetectDefaultDevice() {
			using (var enumerator = new AudioDeviceEnumerator()) {
				try {
					var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

					if (defaultDevice == null) {
						Logger.Warning("No default audio device found");
						return AudioDeviceType.None;
					}

					using (defaultDevice) {
						string friendlyName = defaultDevice.FriendlyName;
						Logger.Info($"Default device FriendlyName: '{friendlyName}'");

						var deviceInfo = new AudioDeviceInfo(defaultDevice.Device);
						string controllerInfo = deviceInfo.ControllerInfo;
						Logger.Info($"Device Controller: '{controllerInfo}'");

						Logger.Info($"Checking for Sonar: '{SonarConfig.DEVICE_SONAR_CONTROLLER_NAME}'");
						bool isSonar = friendlyName.Contains(SonarConfig.DEVICE_SONAR_CONTROLLER_NAME, StringComparison.OrdinalIgnoreCase) ||
											controllerInfo.Contains(SonarConfig.DEVICE_SONAR_CONTROLLER_NAME, StringComparison.OrdinalIgnoreCase);

						Logger.Info($"Checking for FxSound: '{FxSoundConfig.DEVICE_FXSOUND_CONTROLLER_NAME}' or '{FxSoundConfig.DEVICE_FXSOUND_PREFIX}'");
						bool isFxSound = friendlyName.Contains(FxSoundConfig.DEVICE_FXSOUND_CONTROLLER_NAME, StringComparison.OrdinalIgnoreCase) ||
											  friendlyName.Contains(FxSoundConfig.DEVICE_FXSOUND_PREFIX, StringComparison.OrdinalIgnoreCase) ||
											  controllerInfo.Contains(FxSoundConfig.DEVICE_FXSOUND_CONTROLLER_NAME, StringComparison.OrdinalIgnoreCase) ||
											  controllerInfo.Contains(FxSoundConfig.DEVICE_FXSOUND_PREFIX, StringComparison.OrdinalIgnoreCase);

						if (isSonar) {
							Logger.Info("✓ Using SteelSeries Sonar");
							return AudioDeviceType.SteelSeriesSonar;
						}

						if (isFxSound) {
							Logger.Info("✓ Using FxSound");
							return AudioDeviceType.FxSound;
						}

						Logger.Info("Device not compatible - waiting...");
						return AudioDeviceType.None;
					}
				} catch (Exception ex) {
					Logger.Error($"Error detecting default device: {ex.Message}");
					return AudioDeviceType.None;
				}
			}
		}
	}
}
