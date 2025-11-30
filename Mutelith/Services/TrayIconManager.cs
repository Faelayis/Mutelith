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
			contextMenu.Items.Add("Show Logs", null, (s, e) => ShowLogsFolder());
			contextMenu.Items.Add("GitHub", null, (s, e) => OpenGitHubRepository());
			contextMenu.Items.Add(new ToolStripSeparator());
			contextMenu.Items.Add("Exit", null, exitHandler);

			trayIcon.ContextMenuStrip = contextMenu;
			return trayIcon;
		}

		private static void ShowLogsFolder() {
			try {
				if (System.IO.Directory.Exists(AppConstants.INSTALL_FOLDER)) {
					System.Diagnostics.Process.Start("explorer.exe", AppConstants.INSTALL_FOLDER);
				}
			} catch (Exception ex) {
				Logger.Error($"Error opening logs folder: {ex.Message}");
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