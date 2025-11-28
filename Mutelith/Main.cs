using Mutelith;
using Microsoft.Win32;
using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

class Program {
	private static NotifyIcon trayIcon;
	private static SonarManager audioManager;
	private static bool isRunning = true;
	private static bool isSilent = true;
	private static readonly string INSTALL_FOLDER = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"Mutelith"
	);
	private static readonly string INSTALL_PATH = Path.Combine(INSTALL_FOLDER, "mutelith.exe");
	private static readonly string STARTUP_KEY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
	private static readonly string APP_NAME = "Mutelith";

	[STAThread]
	private static void Main(string[] args) {
		bool devMode = Array.Exists(
			args,
			arg => string.Equals(arg, "--dev", StringComparison.OrdinalIgnoreCase)
		);

		bool silentMode = Array.Exists(
			args,
			arg => string.Equals(arg, "--silent", StringComparison.OrdinalIgnoreCase)
		);

		isSilent = silentMode;

		if (!devMode && !IsInstalled()) {
			if (InstallAndRestart()) {
				return;
			}
		}

		trayIcon = new NotifyIcon() {
			Icon = SystemIcons.Application,
			Visible = true,
			Text = devMode
				? "Mutelith (DEV) - Monitoring Discord"
				: "Mutelith - Monitoring Discord"
		};

		var contextMenu = new ContextMenuStrip();
		var statusItem = new ToolStripMenuItem("Status: Starting...");

		statusItem.Enabled = false;
		contextMenu.Items.Add(statusItem);
		contextMenu.Items.Add(new ToolStripSeparator());
		contextMenu.Items.Add("Show Logs", null, (s, e) => {
			ShowLogsFolder();
		});
		contextMenu.Items.Add("Exit", null, (s, e) => {
			isRunning = false;
			trayIcon.Visible = false;
			Application.Exit();
			Environment.Exit(0);
		});

		trayIcon.ContextMenuStrip = contextMenu;

		if (!isSilent) {
			trayIcon.BalloonTipTitle = "Mutelith";
			trayIcon.BalloonTipText = devMode ? "Running in DEV mode" : "Running in background";
			trayIcon.ShowBalloonTip(2000);
		}

		_ = Task.Run(async () => await RunMonitoringLoop(args, statusItem));
		Application.Run();
	}

	static string GetCurrentExecutablePath() {
		if (!string.IsNullOrEmpty(Environment.ProcessPath)) {
			return Environment.ProcessPath;
		}

		string processName = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
		if (!string.IsNullOrEmpty(processName)) {
			return processName;
		}


		return Path.Combine(AppContext.BaseDirectory, "mutelith.exe");
	}

	static bool IsInstalled() {
		string currentPath = GetCurrentExecutablePath();

		if (currentPath.Equals(INSTALL_PATH, StringComparison.OrdinalIgnoreCase)) {
			return true;
		}

		return false;
	}

	static bool InstallAndRestart() {
		try {
			string currentPath = GetCurrentExecutablePath();

			LogToFile($"Installing from: {currentPath}");
			LogToFile($"Installing to: {INSTALL_PATH}");

			Directory.CreateDirectory(INSTALL_FOLDER);

			File.Copy(currentPath, INSTALL_PATH, true);
			LogToFile("File copied successfully");

			using (RegistryKey key = Registry.CurrentUser.OpenSubKey(STARTUP_KEY, true)) {
				if (key != null) {
					key.SetValue(APP_NAME, $"\"{INSTALL_PATH}\" --silent");
					LogToFile("Added to startup registry with --silent");
				}
			}

			var tempIcon = new NotifyIcon() {
				Icon = SystemIcons.Application,
				Visible = true,
				BalloonTipTitle = "Mutelith Installed",
				BalloonTipText = "Application installed successfully. Restarting...",
				BalloonTipIcon = ToolTipIcon.Info
			};
			tempIcon.ShowBalloonTip(2000);

			Thread.Sleep(2500);
			tempIcon.Visible = false;
			tempIcon.Dispose();

			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
				FileName = INSTALL_PATH,
				UseShellExecute = true
			});

			LogToFile("Started new instance from install path");
			return true;

		} catch (Exception ex) {
			LogToFile($"Installation failed: {ex.Message}");
			MessageBox.Show(
				$"Failed to install Mutelith:\n{ex.Message}\n\nPlease run as administrator or check permissions.",
				"Installation Error",
				MessageBoxButtons.OK,
				MessageBoxIcon.Error
			);
			return false;
		}
	}

	static async Task RunMonitoringLoop(string[] args, ToolStripMenuItem statusItem) {
		try {
			var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
			bool devMode = Array.Exists(args, arg => arg == "--dev");

			LogToFile($"Version: {version}");
			LogToFile($"Running from: {GetCurrentExecutablePath()}");
			if (devMode) {
				LogToFile("DEV MODE: Install check skipped");
			}
			UpdateStatus(statusItem, "Initializing...");

			string githubToken = null;

			for (int i = 0; i < args.Length; i++) {
				if (args[i] == "--ghtoken" && i + 1 < args.Length) {
					githubToken = args[i + 1];
					LogToFile("GitHub token provided");
					break;
				}
			}

			var updateChecker = new UpdateChecker(githubToken);
			bool shouldExit = await updateChecker.CheckForUpdatesAsync();

			if (shouldExit) {
				isRunning = false;
				Application.Exit();
				return;
			}

			audioManager = new SonarManager();
			bool wasDiscordRunning = false;
			bool hasInitialized = false;

			UpdateStatus(statusItem, "Monitoring Discord...");

			while (isRunning) {
				try {
					bool isDiscordRunning = DiscordDetector.IsRunning();

					if (isDiscordRunning != wasDiscordRunning) {
						if (isDiscordRunning) {
							int instanceCount = DiscordDetector.GetInstanceCount();
							string message = $"Discord started ({instanceCount} instance{(instanceCount > 1 ? "s" : "")})";
							LogToFile(message);
							UpdateStatus(statusItem, $"Active - {instanceCount} Discord instance(s)");

							if (!hasInitialized) {
								audioManager.InitializeAndSaveConfigs();
								hasInitialized = true;
							}
							else {
								audioManager.ApplyMuteSettings();
							}
						}
						else {
							LogToFile("Discord stopped - restoring settings");
							UpdateStatus(statusItem, "Waiting for Discord...");
						}
						wasDiscordRunning = isDiscordRunning;
					}
					else if (isDiscordRunning) {
						audioManager.ApplyMuteSettings();
					}

					Thread.Sleep(2000);
				} catch (Exception ex) {
					LogToFile($"Error in monitoring loop: {ex.Message}");
					Thread.Sleep(5000);
				}
			}
		} catch (Exception ex) {
			LogToFile($"Fatal error: {ex.Message}");
		}
	}

	static void UpdateStatus(ToolStripMenuItem statusItem, string status) {
		if (statusItem.GetCurrentParent()?.InvokeRequired == true) {
			statusItem.GetCurrentParent().Invoke(new Action(() => {
				statusItem.Text = $"Status: {status}";
			}));
		}
		else {
			statusItem.Text = $"Status: {status}";
		}
	}

	static void ShowNotification(string title, string message, ToolTipIcon icon) {
		if (trayIcon != null) {
			trayIcon.BalloonTipTitle = title;
			trayIcon.BalloonTipText = message;
			trayIcon.BalloonTipIcon = icon;
			trayIcon.ShowBalloonTip(3000);
		}
	}

	static void LogToFile(string message) {
		try {
			string logPath = Path.Combine(INSTALL_FOLDER, "logs.txt");
			Directory.CreateDirectory(Path.GetDirectoryName(logPath));
			string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
			File.AppendAllText(logPath, logEntry + Environment.NewLine);
		} catch {
		}
	}

	static void ShowLogsFolder() {
		try {
			if (Directory.Exists(INSTALL_FOLDER)) {
				System.Diagnostics.Process.Start("explorer.exe", INSTALL_FOLDER);
			}
		} catch (Exception ex) {
			ShowNotification("Error", $"Cannot open logs: {ex.Message}", ToolTipIcon.Error);
		}
	}
}