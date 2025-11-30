using Mutelith.Audio.CoreAudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Mutelith {
	public abstract class ViewerEcho : IAudioManager {
		private readonly AudioDeviceEnumerator _enumerator;
		private readonly string _configPath;
		private Dictionary<string, AudioSessionConfig> _savedConfigs;
		protected abstract string TargetDeviceName { get; }
		protected abstract string DevicePrefix { get; }
		protected abstract string ConfigFileName { get; }
		protected ViewerEcho() {
			_enumerator = new AudioDeviceEnumerator();
			_savedConfigs = new Dictionary<string, AudioSessionConfig>();

			if (!Directory.Exists(AppConstants.INSTALL_FOLDER)) {
				Directory.CreateDirectory(AppConstants.INSTALL_FOLDER);
			}
			_configPath = Path.Combine(AppConstants.INSTALL_FOLDER, ConfigFileName);
		}

		public void InitializeAndSaveConfigs() {
			bool deviceFound = CheckTargetDeviceExists();

			if (!deviceFound) {
				Logger.Warning($"{TargetDeviceName} not found - restoring default settings");
				RestoreDefaultConfigs();
				return;
			}

			SaveDefaultConfigs();
			FindAndConfigureTargetDevice();
		}

		public void ApplyMuteSettings() {
			bool deviceFound = CheckTargetDeviceExists();

			if (!deviceFound) {
				Logger.Warning($"{TargetDeviceName} not found");
				return;
			}

			FindAndConfigureTargetDevice();
		}

		private AudioDevice GetTargetDevice() {
			foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)) {
				if (device.FriendlyName.Contains(TargetDeviceName, StringComparison.OrdinalIgnoreCase)) {
					return device;
				} else {
					device.Dispose();
				}
			}

			return null;
		}

		private bool CheckTargetDeviceExists() {
			var device = GetTargetDevice();
			if (device != null) {
				device.Dispose();
				return true;
			}
			return false;
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

		private bool FindAndConfigureTargetDevice() {
			int discordMutedCount = 0;
			var targetDevice = GetTargetDevice();

			if (targetDevice != null) {
				discordMutedCount += MuteDiscordInDevice(targetDevice);
				targetDevice.Dispose();
			}

			foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)) {
				if (device.FriendlyName.Contains(DevicePrefix, StringComparison.OrdinalIgnoreCase)) {
					device.Dispose();
					continue;
				}

				discordMutedCount += MuteDiscordInDevice(device);
				device.Dispose();
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

		public void RestoreSettings() {
			RestoreDefaultConfigs();
		}

		public void Dispose() {
			_enumerator?.Dispose();
		}
	}
}