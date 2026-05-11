using Mutelith.Audio.CoreAudio;
using System;
using System.Collections.Generic;

namespace Mutelith {
	public abstract class ViewerEcho : IAudioManager {
		private readonly AudioDeviceEnumerator _enumerator;
		private HashSet<uint> _mutedProcessIds;
		private bool _lastDiscordFoundState = false;
		private int _lastDiscordMutedCount = 0;
		protected abstract string TargetDeviceName { get; }
		protected abstract string DevicePrefix { get; }
		protected ViewerEcho() {
			_enumerator = new AudioDeviceEnumerator();
			_mutedProcessIds = new HashSet<uint>();
		}

		public void InitializeAndSaveConfigs() {
			_mutedProcessIds.Clear();
			bool deviceFound = CheckTargetDeviceExists();

			if (!deviceFound) {
				Logger.Warning($"{TargetDeviceName} not found");
				return;
			}

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

			bool discordFound = discordMutedCount > 0;

			if (discordFound != _lastDiscordFoundState || discordMutedCount != _lastDiscordMutedCount) {
				if (discordFound) {
					Logger.Info($"Total Discord sessions muted: {discordMutedCount}");
				} else {
					Logger.Info("Waiting for Discord audio session (e.g., screen share, streaming)");
				}
				_lastDiscordFoundState = discordFound;
				_lastDiscordMutedCount = discordMutedCount;
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
							continue;
						}

						using var process = System.Diagnostics.Process.GetProcessById((int)processId);

						if (!IsDiscordProcess(process.ProcessName)) {
							continue;
						}

						if (_mutedProcessIds.Contains(processId) && session.Mute) {
							mutedCount++;
							continue;
						}

						bool isNewMute = !_mutedProcessIds.Contains(processId);
						session.Volume = 0f;
						session.Mute = true;
						_mutedProcessIds.Add(processId);
						mutedCount++;

						if (isNewMute) {
							Logger.Success($"{process.ProcessName} (PID: {processId}) muted in {device.FriendlyName}");
						}
					} catch {
					} finally {
						session.Dispose();
					}
				}
			}

			return mutedCount;
		}

		public void RestoreSettings() {
			UnmuteAllDiscord();
			_mutedProcessIds.Clear();
		}

		private void UnmuteAllDiscord() {
			if (_mutedProcessIds.Count == 0) {
				return;
			}

			int unmutedCount = 0;

			foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)) {
				if (device.FriendlyName.Contains(DevicePrefix, StringComparison.OrdinalIgnoreCase) &&
					!device.FriendlyName.Contains(TargetDeviceName, StringComparison.OrdinalIgnoreCase)) {
					device.Dispose();
					continue;
				}

				unmutedCount += UnmuteDiscordInDevice(device);
				device.Dispose();
			}

			if (unmutedCount > 0) {
				Logger.Success($"Unmuted {unmutedCount} Discord session(s)");
			} else {
				Logger.Info("No Discord sessions to unmute");
			}
		}

		private int UnmuteDiscordInDevice(AudioDevice device) {
			int unmutedCount = 0;

			using (var sessionManager = device.GetAudioSessionManager()) {
				if (sessionManager == null) {
					return unmutedCount;
				}

				foreach (var session in sessionManager.GetSessions()) {
					try {
						var processId = session.ProcessId;
						if (processId == 0 || !_mutedProcessIds.Contains(processId)) {
							continue;
						}

						using var process = System.Diagnostics.Process.GetProcessById((int)processId);

						if (!IsDiscordProcess(process.ProcessName)) {
							continue;
						}

						session.Mute = false;
						session.Volume = 1.0f;
						unmutedCount++;
						Logger.Success($"{process.ProcessName} (PID: {processId}) unmuted in {device.FriendlyName}");
					} catch {
					} finally {
						session.Dispose();
					}
				}
			}

			return unmutedCount;
		}

		private static bool IsDiscordProcess(string processName) {
			return processName.Equals(DiscordDetector.PROCESS_NAME_DISCORD, StringComparison.OrdinalIgnoreCase) ||
				processName.Equals(DiscordDetector.PROCESS_NAME_DISCORD_PTB, StringComparison.OrdinalIgnoreCase) ||
				processName.Equals(DiscordDetector.PROCESS_NAME_DISCORD_DEV, StringComparison.OrdinalIgnoreCase);
		}

		public void Dispose() {
			_enumerator?.Dispose();
		}
	}
}
