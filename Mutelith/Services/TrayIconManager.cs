using System;
using System.Windows.Forms;

namespace Mutelith {
	public static class TrayIconManager {
		public static NotifyIcon CreateTrayIcon(EventHandler exitHandler) {
			var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
			var versionText = $"v{version.Major}.{version.Minor}.{version.Build}";
			var trayIcon = new NotifyIcon() {
				Icon = System.Drawing.SystemIcons.Application,
				Visible = true,
				Text = $"Mutelith {versionText}"
			};

			var contextMenu = new ContextMenuStrip();
			var versionItem = new ToolStripMenuItem($"Mutelith {versionText}") {
				Enabled = false
			};
			contextMenu.Items.Add(versionItem);
			contextMenu.Items.Add(new ToolStripSeparator());

			if (System.IO.File.Exists(AppConstants.LOG_PATH)) {
				contextMenu.Items.Add("Show Logs", null, (s, e) => ShowLogsFile());
			}

			contextMenu.Items.Add("GitHub", null, (s, e) => OpenGitHubRepository());
			contextMenu.Items.Add(new ToolStripSeparator());
			contextMenu.Items.Add("Exit", null, exitHandler);

			trayIcon.ContextMenuStrip = contextMenu;
			return trayIcon;
		}

		private static void ShowLogsFile() {
			try {
				if (System.IO.File.Exists(AppConstants.LOG_PATH)) {
					System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
						FileName = AppConstants.LOG_PATH,
						UseShellExecute = true
					});
				}
			} catch (Exception ex) {
				Logger.Error($"Error opening log file: {ex.Message}");
			}
		}

		private static void OpenGitHubRepository() {
			try {
				System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
					FileName = AppConstants.GITHUB_REPOSITORY_URL,
					UseShellExecute = true
				});
			} catch (Exception ex) {
				Logger.Error($"Error opening GitHub repository: {ex.Message}");
			}
		}

		public static void SetDevModeText(NotifyIcon trayIcon) {
			var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
			var versionText = $"v{version.Major}.{version.Minor}.{version.Build}";
			trayIcon.Text = $"Mutelith {versionText} (DEV)";
		}
	}
}