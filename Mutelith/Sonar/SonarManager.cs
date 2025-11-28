using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Mutelith {
	public class SonarManager {
		private readonly MMDeviceEnumerator _enumerator;
		private readonly string _configPath = "sonar_config.json";
		private Dictionary<string, AudioSessionConfig> _savedConfigs;
		private MMDevice _sonarMicDevice;
		private DateTime _lastDeviceCacheTime;
		private readonly TimeSpan _cacheExpiration = TimeSpan.FromSeconds(30);

		public SonarManager() {
			_enumerator = new MMDeviceEnumerator();
			_savedConfigs = new Dictionary<string, AudioSessionConfig>();
			_lastDeviceCacheTime = DateTime.MinValue;
		}

		public void InitializeAndSaveConfigs() {
			bool sonarMicFound = CheckSonarMicrophoneExists();

			if (!sonarMicFound) {
				Logger.Warning("SteelSeries Sonar - Microphone not found - restoring default settings");
				RestoreDefaultConfigs();
				return;
			}

			SaveDefaultConfigs();
			MuteDiscordInDevices();
		}

		public void ApplyMuteSettings() {
			bool sonarMicFound = CheckSonarMicrophoneExists();

			if (!sonarMicFound) {
				Logger.Warning("SteelSeries Sonar - Microphone not found");
				return;
			}

			MuteDiscordInDevices();
		}

		private MMDevice GetSonarMicrophoneDevice(bool forceRefresh = false) {
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

			_sonarMicDevice = null;
			foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)) {
				if (device.FriendlyName.Contains("SteelSeries Sonar - Microphone", StringComparison.OrdinalIgnoreCase)) {
					_sonarMicDevice = device;
					_lastDeviceCacheTime = DateTime.Now;
					Logger.Info($"Found and cached device: {device.FriendlyName}");
					break;
				}
			}

			return _sonarMicDevice;
		}

		private bool CheckSonarMicrophoneExists() {
			return GetSonarMicrophoneDevice() != null;
		}

		private void SaveDefaultConfigs() {
			Logger.Info("Saving default audio configurations...");
			_savedConfigs.Clear();

			foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)) {
				var sessions = device.AudioSessionManager.Sessions;
				for (int i = 0; i < sessions.Count; i++) {
					var session = sessions[i];
					var processId = session.GetProcessID;

					try {
						var process = System.Diagnostics.Process.GetProcessById((int)processId);
						string key = $"{device.FriendlyName}_{process.ProcessName}_{processId}";

						var config = new AudioSessionConfig {
							DeviceName = device.FriendlyName,
							ProcessName = process.ProcessName,
							ProcessId = processId,
							Volume = session.SimpleAudioVolume.Volume,
							IsMuted = session.SimpleAudioVolume.Mute
						};

						_savedConfigs[key] = config;
					} catch {
					}
				}
			}

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
				if (device.FriendlyName.Contains("SteelSeries Sonar", StringComparison.OrdinalIgnoreCase)) {
					continue;
				}

				discordMutedCount += MuteDiscordInDevice(device);
			}

			if (discordMutedCount > 0) {
				Logger.Info($"Total Discord sessions muted: {discordMutedCount}");
			}
			else {
				Logger.Warning("Discord audio session not found in any device");
			}

			return true;
		}

		private int MuteDiscordInDevice(MMDevice device) {
			int mutedCount = 0;
			var sessions = device.AudioSessionManager.Sessions;

			for (int i = 0; i < sessions.Count; i++) {
				var session = sessions[i];
				var processId = session.GetProcessID;

				try {
					var process = System.Diagnostics.Process.GetProcessById((int)processId);

					if (process.ProcessName.Equals("Discord", StringComparison.OrdinalIgnoreCase)) {
						session.SimpleAudioVolume.Volume = 0f;
						session.SimpleAudioVolume.Mute = true;
						mutedCount++;
						Logger.Success($"Discord muted in {device.FriendlyName}");
					}
				} catch {
				}
			}

			return mutedCount;
		}

		private void MuteDiscordInDevices() {
			FindAndConfigureSonarMicrophone();
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

			foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)) {
				var sessions = device.AudioSessionManager.Sessions;
				for (int i = 0; i < sessions.Count; i++) {
					var session = sessions[i];
					var processId = session.GetProcessID;

					try {
						var process = System.Diagnostics.Process.GetProcessById((int)processId);
						string key = $"{device.FriendlyName}_{process.ProcessName}_{processId}";

						if (_savedConfigs.TryGetValue(key, out var config)) {
							session.SimpleAudioVolume.Volume = config.Volume;
							session.SimpleAudioVolume.Mute = config.IsMuted;
							restoredCount++;
						}
					} catch {
					}
				}
			}

			Logger.Success($"Restored {restoredCount} audio session configurations");
		}

		public bool MuteDiscordInSonarMicrophone() {
			var sonarMic = GetSonarMicrophoneDevice();

			if (sonarMic == null) {
				Logger.Warning("SteelSeries Sonar - Microphone device not found");
				return false;
			}

			Logger.Info($"Found device: {sonarMic.FriendlyName}");
			int mutedCount = MuteDiscordInDevice(sonarMic);

			if (mutedCount == 0) {
				Logger.Warning("Discord audio session not found in SteelSeries Sonar - Microphone");
				return false;
			}

			Logger.Success("Discord audio in SteelSeries Sonar - Microphone has been muted");
			return true;
		}

		public void ClearDeviceCache() {
			_sonarMicDevice = null;
			_lastDeviceCacheTime = DateTime.MinValue;
		}
	}
}