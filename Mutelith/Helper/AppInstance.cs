using System;
using System.Diagnostics;
using System.Reflection;

namespace Mutelith {
	public static class AppInstance {
		public static void ClosePreviousInstances() {
			try {
				var current = Process.GetCurrentProcess();
				var processes = Process.GetProcessesByName(current.ProcessName);

				foreach (var p in processes) {
					if (p.Id == current.Id) {
						continue;
					}

					try {
						Logger.Info($"Found previous Mutelith instance (PID {p.Id}), killing it...");
						p.Kill();
						p.WaitForExit(5000);
					} catch (Exception ex) {
						Logger.Error($"Failed to kill previous instance {p.Id}: {ex.Message}");
					}
				}
			} catch (Exception ex) {
				Logger.Error($"Error while checking for previous instances: {ex.Message}");
			}
		}
	}
}