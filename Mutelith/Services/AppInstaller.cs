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

			return Path.Combine(AppContext.BaseDirectory, "Mutelith.exe");
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
						key.SetValue(AppConstants.APP_NAME, $"\"{AppConstants.INSTALL_PATH}\" {AppConstants.ARG_STARTUP} {AppConstants.ARG_SILENT_MODE}");
						Logger.Info($"Added to startup registry with {AppConstants.ARG_STARTUP} {AppConstants.ARG_SILENT_MODE}");
					}
				}

				RegisterInProgramsAndFeatures();

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

		private static long GetDirectorySize(string folderPath) {
			if (!Directory.Exists(folderPath)) {
				return 0;
			}

			long size = 0;
			var dirInfo = new DirectoryInfo(folderPath);

			try {
				foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories)) {
					size += file.Length;
				}
			} catch {
			}

			return size;
		}

		private static void RegisterInProgramsAndFeatures() {
			try {
				using (var key = Registry.CurrentUser.CreateSubKey(AppUninstaller.UninstallKeyPath)) {
					if (key == null) {
						Logger.Warning("Cannot create uninstall registry key.");
						return;
					}

					string exePath = AppConstants.INSTALL_PATH;

					key.SetValue("DisplayName", AppConstants.APP_NAME);
					key.SetValue("DisplayVersion", AppConstants.APP_VERSION);
					key.SetValue("Publisher", AppConstants.APP_PUBLISHER);
					key.SetValue("InstallLocation", AppConstants.INSTALL_FOLDER);

					string uninstallCmd = $"\"{exePath}\" {AppConstants.ARG_UNINSTALL}";
					key.SetValue("UninstallString", uninstallCmd);
					key.SetValue("NoModify", 1, RegistryValueKind.DWord);
					key.SetValue("NoRepair", 1, RegistryValueKind.DWord);

					string helpLink = $"{AppConstants.GITHUB_REPOSITORY_URL}/issues";
					key.SetValue("HelpLink", helpLink);

					string supportLink = AppConstants.GITHUB_REPOSITORY_URL;
					key.SetValue("URLInfoAbout", supportLink);

					long sizeBytes = AppInfo.GetDirectorySize(AppConstants.INSTALL_FOLDER);
					int sizeKb = (int)Math.Max(1, sizeBytes / 1024);
					key.SetValue("EstimatedSize", sizeKb, RegistryValueKind.DWord);
				}

				Logger.Info("Registered app in Programs and Features");
			} catch (Exception ex) {
				Logger.Error($"Failed to register in Programs and Features: {ex.Message}");
			}
		}
	}
}