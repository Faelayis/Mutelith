using System;
using System.IO;

namespace Mutelith {
	public static class AppConstants {
		public const string APP_NAME = "Mutelith";
		public static readonly string INSTALL_FOLDER = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			APP_NAME
		);
		public static readonly string INSTALL_PATH = Path.Combine(INSTALL_FOLDER, "mutelith.exe");
		public static readonly string CONFIG_PATH = Path.Combine(INSTALL_FOLDER, "device.json");
		public static readonly string LOG_PATH = Path.Combine(INSTALL_FOLDER, "logs.txt");
		public const string STARTUP_REGISTRY_KEY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
		public const int MONITORING_INTERVAL_MS = 2000;
		public const int ERROR_RETRY_INTERVAL_MS = 5000;
		public const int FULLSCREEN_SKIP_INTERVAL_MS = 2000;
		public const string ARG_DEV_MODE = "--dev";
		public const string ARG_SILENT_MODE = "--silent";
		public const string ARG_STARTUP = "--startup";
		public const string ARG_GH_TOKEN = "--ghtoken";
		public const string ARG_NO_LOGS = "--nologs";
		public const string GITHUB_REPOSITORY_URL = "https://github.com/Faelayis/Mutelith";
	}
}