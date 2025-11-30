using Mutelith.Audio.CoreAudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Mutelith {
	public class SonarManager : IDisposable {
		private readonly AudioDeviceEnumerator _enumerator;
		private readonly string _configPath;
		private Dictionary<string, AudioSessionConfig> _savedConfigs;
		private AudioDevice _sonarMicDevice;
		private DateTime _lastDeviceCacheTime;
		private readonly TimeSpan _cacheExpiration = TimeSpan.FromSeconds(AppConstants.DEVICE_CACHE_EXPIRATION_SECONDS);
		public SonarManager() {
			_enumerator = new AudioDeviceEnumerator();
			_savedConfigs = new Dictionary<string, AudioSessionConfig>();
			_lastDeviceCacheTime = DateTime.MinValue;

			if (!Directory.Exists(AppConstants.INSTALL_FOLDER)) {
				Directory.CreateDirectory(AppConstants.INSTALL_FOLDER);
			}
			_configPath = AppConstants.CONFIG_PATH;
		}

		public void InitializeAndSaveConfigs() {
			bool sonarMicFound = CheckSonarMicrophoneExists();

			if (!sonarMicFound) {
				Logger.Warning("SteelSeries Sonar - Microphone not found - restoring default settings");
				RestoreDefaultConfigs();
				return;
			}

			SaveDefaultConfigs();
			FindAndConfigureSonarMicrophone();
		}

		public void ApplyMuteSettings() {
			bool sonarMicFound = CheckSonarMicrophoneExists();

			if (!sonarMicFound) {
				Logger.Warning("SteelSeries Sonar - Microphone not found");
				return;
			}

			FindAndConfigureSonarMicrophone();
		}

		private AudioDevice GetSonarMicrophoneDevice(bool forceRefresh = false) {
			if (!forceRefresh &&
				 _sonarMicDevice != null &&
				 DateTime.Now - _lastDeviceCacheTime < _cacheExpiration) {
				try {
					var state = _sonarMicDevice.State;
					if (state == DeviceState.Active) {
						return _sonarMicDevice;
					}
				} catch {
				}
			}

			_sonarMicDevice?.Dispose();
			_sonarMicDevice = null;

			foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)) {
				if (device.FriendlyName.Contains(SonarConfig.DEVICE_SONAR_MICROPHONE, StringComparison.OrdinalIgnoreCase)) {
					_sonarMicDevice = device;
					_lastDeviceCacheTime = DateTime.Now;
					Logger.Info($"Found and cached device: {device.FriendlyName}");
					break;
				} else {
					device.Dispose();
				}
			}

			return _sonarMicDevice;
		}

		private bool CheckSonarMicrophoneExists() {
			return GetSonarMicrophoneDevice() != null;
		}

		private void ForEachAudioSession(Action<AudioDevice, AudioSession> action) {
			foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)) {
				using (var sessionManager = device.GetAudioSessionManager()) {
					if (sessionManager == null) {
						device.Dispose();
						continue;
					}

					foreach (var session in sessionManager.GetSessions()) {
						try {
							action(device, session);
							session.Dispose();
						} catch {
							session.Dispose();
						}
					}
				}
				device.Dispose();
			}
		}

		private void SaveDefaultConfigs() {
			Logger.Info("Saving default audio configurations...");
			_savedConfigs.Clear();

			ForEachAudioSession((device, session) => {
				var processId = session.ProcessId;
				if (processId == 0) return;

				var process = System.Diagnostics.Process.GetProcessById((int)processId);
				string key = $"{device.FriendlyName}_{process.ProcessName}_{processId}";

				var config = new AudioSessionConfig {
					DeviceName = device.FriendlyName,
					ProcessName = process.ProcessName,
					ProcessId = processId,
					Volume = session.Volume,
					IsMuted = session.Mute
				};

				_savedConfigs[key] = config;
			});

			try {
				string json = JsonSerializer.Serialize(_savedConfigs, new JsonSerializerOptions { WriteIndented = true });
				File.WriteAllText(_configPath, json);
				Logger.Success($"Saved {_savedConfigs.Count} audio session configurations");
			} catch (Exception ex) {
				Logger.Error($"Failed to save config file: {ex.Message}");
			}
		}

		private bool FindAndConfigureSonarMicrophone() {
			int discordMutedCount = 0;
			var sonarMic = GetSonarMicrophoneDevice();

			if (sonarMic != null) {
				discordMutedCount += MuteDiscordInDevice(sonarMic);
			}

			foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)) {
				if (device.FriendlyName.Contains(SonarConfig.DEVICE_SONAR_PREFIX, StringComparison.OrdinalIgnoreCase)) {
					continue;
				}

				discordMutedCount += MuteDiscordInDevice(device);
			}

			if (discordMutedCount > 0) {
				Logger.Info($"Total Discord sessions muted: {discordMutedCount}");
			} else {
				Logger.Warning("Discord audio session not found in any device");
			}

			return true;
		}

		private int MuteDiscordInDevice(AudioDevice device) {
			int mutedCount = 0;

			using (var sessionManager = device.GetAudioSessionManager()) {
				if (sessionManager == null) {
					return mutedCount;
				}

				foreach (var session in sessionManager.GetSessions()) {
					try {
						var processId = session.ProcessId;
						if (processId == 0) {
							session.Dispose();
							continue;
						}

						var process = System.Diagnostics.Process.GetProcessById((int)processId);

						if (process.ProcessName.Equals(DiscordDetector.PROCESS_NAME_DISCORD, StringComparison.OrdinalIgnoreCase) ||
							process.ProcessName.Equals(DiscordDetector.PROCESS_NAME_DISCORD_PTB, StringComparison.OrdinalIgnoreCase) ||
							process.ProcessName.Equals(DiscordDetector.PROCESS_NAME_DISCORD_DEV, StringComparison.OrdinalIgnoreCase)) {
							session.Volume = 0f;
							session.Mute = true;
							mutedCount++;
							Logger.Success($"{process.ProcessName} muted in {device.FriendlyName}");
						}

						session.Dispose();
					} catch {
					}
				}
			}

			return mutedCount;
		}

		private void RestoreDefaultConfigs() {
			if (_savedConfigs.Count == 0) {
				try {
					if (File.Exists(_configPath)) {
						string json = File.ReadAllText(_configPath);
						_savedConfigs = JsonSerializer.Deserialize<Dictionary<string, AudioSessionConfig>>(json)
										?? new Dictionary<string, AudioSessionConfig>();
					}
				} catch (Exception ex) {
					Logger.Error($"Failed to load config file: {ex.Message}");
					return;
				}
			}

			if (_savedConfigs.Count == 0) {
				Logger.Warning("No saved configurations to restore");
				return;
			}

			Logger.Info("Restoring default audio configurations...");
			int restoredCount = 0;

			ForEachAudioSession((device, session) => {
				var processId = session.ProcessId;
				if (processId == 0) return;

				var process = System.Diagnostics.Process.GetProcessById((int)processId);
				string key = $"{device.FriendlyName}_{process.ProcessName}_{processId}";

				if (_savedConfigs.TryGetValue(key, out var config)) {
					session.Volume = config.Volume;
					session.Mute = config.IsMuted;
					restoredCount++;
				}
			});

			Logger.Success($"Restored {restoredCount} audio session configurations");
		}

		public void ClearDeviceCache() {
			_sonarMicDevice?.Dispose();
			_sonarMicDevice = null;
			_lastDeviceCacheTime = DateTime.MinValue;
		}

		public void Dispose() {
			_sonarMicDevice?.Dispose();
			_enumerator?.Dispose();
		}
	}
}