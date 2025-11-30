using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;

namespace Mutelith {
	public static class AppUninstaller {
		public static string UninstallKeyPath =>
			$@"{AppConstants.UNINSTALL_REGISTRY_KEY}\{AppConstants.APP_NAME}";
		public static void UninstallAndRemoveFile() {
			UnregisterFromProgramsAndFeatures();
			ScheduleDeleteInstallFolder();
		}

		public static void UnregisterFromProgramsAndFeatures() {
			try {
				Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);

				using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AppConstants.STARTUP_REGISTRY_KEY, writable: true)) {
					key?.DeleteValue(AppConstants.APP_NAME, throwOnMissingValue: false);
				}

				Logger.Info("Unregistered app from Programs and Features and Startup");
			} catch (Exception ex) {
				Logger.Error($"Failed to unregister from Programs and Features: {ex.Message}");
			}
		}

		private static void ScheduleDeleteInstallFolder() {
			try {
				string installFolder = AppConstants.INSTALL_FOLDER;

				if (string.IsNullOrEmpty(installFolder) || !Directory.Exists(installFolder)) {
					Logger.Info("Install folder does not exist or is empty, skipping folder delete.");
					return;
				}

				string batchPath = Path.Combine(Path.GetTempPath(), "mutelith_uninstall.bat");
				string batchContent = $@"@echo off
timeout /t 2 /nobreak > nul
rmdir /s /q ""{installFolder}""
del ""%~f0""
";

				File.WriteAllText(batchPath, batchContent);

				Process.Start(new ProcessStartInfo {
					FileName = batchPath,
					CreateNoWindow = true,
					UseShellExecute = false
				});

				Logger.Info($"Scheduled install folder deletion via: {batchPath}");
			} catch (Exception ex) {
				Logger.Error($"Failed to schedule install folder deletion: {ex.Message}");
			}
		}
	}
}