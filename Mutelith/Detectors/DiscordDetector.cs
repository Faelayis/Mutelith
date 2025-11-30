using System;
using System.Diagnostics;

namespace Mutelith {
	public static class DiscordDetector {
		public const string PROCESS_NAME_DISCORD = "Discord";
		public const string PROCESS_NAME_DISCORD_PTB = "DiscordPTB";
		public const string PROCESS_NAME_DISCORD_DEV = "DiscordDevelopment";

		public static bool IsRunning() {
			return GetInstanceCount() > 0;
		}

		public static int GetInstanceCount() {
			try {
				var discordProcesses = Process.GetProcessesByName(PROCESS_NAME_DISCORD);
				var discordPtbProcesses = Process.GetProcessesByName(PROCESS_NAME_DISCORD_PTB);
				var discordDevProcesses = Process.GetProcessesByName(PROCESS_NAME_DISCORD_DEV);

				return discordProcesses.Length + discordPtbProcesses.Length + discordDevProcesses.Length;
			} catch {
				return 0;
			}
		}
	}
}