using System;
using System.IO;

namespace Mutelith {
	public static class Logger {
		private static bool _devMode = false;
		private static bool _logs = false;

		public static void Initialize(bool devMode = false, bool logs = false) {
			_devMode = devMode;
			_logs = logs;
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
			try {
				string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";

				if (_devMode) {
					Console.WriteLine(logEntry);
					System.Diagnostics.Debug.WriteLine(logEntry);
				}

				if (_logs) {
					Directory.CreateDirectory(Path.GetDirectoryName(AppConstants.LOG_PATH));
					File.AppendAllText(AppConstants.LOG_PATH, logEntry + Environment.NewLine);
				}
			} catch {
			}
		}
	}
}