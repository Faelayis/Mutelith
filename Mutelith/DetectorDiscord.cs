using System;
using System.Diagnostics;

namespace Mutelith {
	public static class DiscordDetector {
		public static bool IsRunning() {
			try {
				var processes = Process.GetProcessesByName("Discord");
				return processes.Length > 0;
			} catch {
				return false;
			}
		}

		public static int GetInstanceCount() {
			try {
				var processes = Process.GetProcessesByName("Discord");
				return processes.Length;
			} catch {
				return 0;
			}
		}
	}
}