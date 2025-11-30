using Microsoft.Win32;
using System;
using System.IO;
using System.Windows.Forms;

namespace Mutelith {
	public static class AppInstaller {
		public static string GetCurrentExecutablePath() {
			if (!string.IsNullOrEmpty(Environment.ProcessPath)) {
				return Environment.ProcessPath;
			}

			string processName = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
			if (!string.IsNullOrEmpty(processName)) {
				return processName;
			}

			return Path.Combine(AppContext.BaseDirectory, "mutelith.exe");
		}

		public static bool IsInstalled() {
			string currentPath = GetCurrentExecutablePath();
			return currentPath.Equals(AppConstants.INSTALL_PATH, StringComparison.OrdinalIgnoreCase);
		}

		public static bool InstallAndRestart() {
			try {
				string currentPath = GetCurrentExecutablePath();

				Logger.Info($"Installing from: {currentPath}");
				Logger.Info($"Installing to: {AppConstants.INSTALL_PATH}");

				Directory.CreateDirectory(AppConstants.INSTALL_FOLDER);
				File.Copy(currentPath, AppConstants.INSTALL_PATH, true);
				Logger.Info("File copied successfully");

				using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AppConstants.STARTUP_REGISTRY_KEY, true)) {
					if (key != null) {
						key.SetValue(AppConstants.APP_NAME, $"\"{AppConstants.INSTALL_PATH}\" {AppConstants.ARG_SILENT_MODE} {AppConstants.ARG_STARTUP}");
						Logger.Info($"Added to startup registry with {AppConstants.ARG_SILENT_MODE} {AppConstants.ARG_STARTUP}");
					}
				}

				System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
					FileName = AppConstants.INSTALL_PATH,
					UseShellExecute = true
				});

				Logger.Info("Started new instance from install path");
				return true;

			} catch (Exception ex) {
				Logger.Error($"Installation failed: {ex.Message}");
				MessageBox.Show(
					$"Failed to install {AppConstants.APP_NAME}:\n{ex.Message}\n\nPlease run as administrator or check permissions.",
					"Installation Error",
					MessageBoxButtons.OK,
					MessageBoxIcon.Error
				);
				return false;
			}
		}
	}
}
