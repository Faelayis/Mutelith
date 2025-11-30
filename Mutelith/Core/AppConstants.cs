using System;
using System.IO;

namespace Mutelith {
	public static class AppConstants {
		public static readonly string INSTALL_FOLDER = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), APP_NAME);
		public static readonly string INSTALL_PATH = Path.Combine(INSTALL_FOLDER, "Mutelith.exe");
		public static readonly string APP_VERSION = AppInfo.GetDisplayVersion();
		public static readonly string LOG_PATH = Path.Combine(INSTALL_FOLDER, "logs.txt");
		public const int MONITORING_INTERVAL_MS = 2000;
		public const int ERROR_RETRY_INTERVAL_MS = 5000;
		public const int FULLSCREEN_SKIP_INTERVAL_MS = 2000;
		public const string APP_NAME = "Mutelith";
		public const string APP_PUBLISHER = "Faelayis";
		public const string ARG_DEV_MODE = "--dev";
		public const string ARG_SILENT_MODE = "--silent";
		public const string ARG_STARTUP = "--startup";
		public const string ARG_GH_TOKEN = "--ghtoken";
		public const string ARG_LOGS = "--logs";
		public const string ARG_UNINSTALL = "--uninstall";
		public const string GITHUB_REPOSITORY_URL = "https://github.com/Faelayis/Mutelith";
		public const string STARTUP_REGISTRY_KEY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
		public const string UNINSTALL_REGISTRY_KEY = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
	}
}