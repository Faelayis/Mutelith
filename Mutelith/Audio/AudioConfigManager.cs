using Mutelith.Audio.CoreAudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Mutelith {
	public class AudioConfigManager {
		private readonly string _configPath;
		private Dictionary<string, AudioSessionConfig> _savedConfigs;

		public AudioConfigManager(string configFileName) {
			if (!Directory.Exists(AppConstants.INSTALL_FOLDER)) {
				Directory.CreateDirectory(AppConstants.INSTALL_FOLDER);
			}
			_configPath = Path.Combine(AppConstants.INSTALL_FOLDER, configFileName);
			_savedConfigs = new Dictionary<string, AudioSessionConfig>();
		}

		public void SaveConfigs(AudioDeviceEnumerator enumerator) {
			Logger.Info("Saving Discord audio configurations...");
			_savedConfigs.Clear();

			ForEachAudioSession(enumerator, (device, session) => {
				var processId = session.ProcessId;
				if (processId == 0) return;

				var process = System.Diagnostics.Process.GetProcessById((int)processId);

				if (!process.ProcessName.Equals(DiscordDetector.PROCESS_NAME_DISCORD, StringComparison.OrdinalIgnoreCase) &&
					!process.ProcessName.Equals(DiscordDetector.PROCESS_NAME_DISCORD_PTB, StringComparison.OrdinalIgnoreCase) &&
					!process.ProcessName.Equals(DiscordDetector.PROCESS_NAME_DISCORD_DEV, StringComparison.OrdinalIgnoreCase)) {
					return;
				}

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
				Logger.Success($"Saved {_savedConfigs.Count} Discord audio session configurations");
			} catch (Exception ex) {
				Logger.Error($"Failed to save config file: {ex.Message}");
			}
		}

		public void RestoreConfigs(AudioDeviceEnumerator enumerator) {
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

			Logger.Info("Restoring Discord audio configurations...");
			int restoredCount = 0;

			ForEachAudioSession(enumerator, (device, session) => {
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

			Logger.Success($"Restored {restoredCount} Discord audio session configurations");
		}

		private void ForEachAudioSession(AudioDeviceEnumerator enumerator, Action<AudioDevice, AudioSession> action) {
			foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)) {
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
	}
}
