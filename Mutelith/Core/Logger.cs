using System;
using System.IO;

namespace Mutelith {
	public static class Logger {
		private static bool _devMode = false;
		private static bool _noLogs = false;

		public static void Initialize(bool devMode = false, bool noLogs = false) {
			_devMode = devMode;
			_noLogs = noLogs;
		}

		public static void Info(string message) {
			LogMessage(message);
		}

		public static void Success(string message) {
			LogMessage(message);
		}

		public static void Warning(string message) {
			LogMessage(message);
		}

		public static void Error(string message) {
			LogMessage(message);
		}

		private static void LogMessage(string message) {
			if (_noLogs) return;

			try {
				string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";

				if (_devMode) {
					Console.WriteLine(logEntry);
					System.Diagnostics.Debug.WriteLine(logEntry);
				}

				Directory.CreateDirectory(Path.GetDirectoryName(AppConstants.LOG_PATH));
				File.AppendAllText(AppConstants.LOG_PATH, logEntry + Environment.NewLine);
			} catch {
			}
		}
	}
}