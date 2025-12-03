using Mutelith;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

class Program {
	private static NotifyIcon trayIcon;
	private static IAudioManager audioManager;
	private static bool isRunning = true;
	private static bool isSilent = false;

	[STAThread]
	private static void Main(string[] args) {
		Debug.WriteLine($"Args received: {string.Join(", ", args)}");

		bool devMode = Array.Exists(args, arg => arg == AppConstants.ARG_DEV_MODE);
		bool silentMode = Array.Exists(args, arg => arg == AppConstants.ARG_SILENT_MODE);
		bool logs = Array.Exists(args, arg => arg == AppConstants.ARG_LOGS);
		bool uninstall = Array.Exists(args, arg => string.Equals(arg, AppConstants.ARG_UNINSTALL, StringComparison.OrdinalIgnoreCase));

		Logger.Initialize(devMode, logs);
		isSilent = silentMode;

		Debug.WriteLine($"Dev Mode: {devMode}, Silent Mode: {silentMode}");
		AppInstance.ClosePreviousInstances();

		if (uninstall) {
			AppUninstaller.UninstallAndRemoveFile();
			return;
		}

		if (!devMode && !AppInstaller.IsInstalled()) {
			if (AppInstaller.InstallAndRestart()) {
				return;
			}
		}

		trayIcon = TrayIconManager.CreateTrayIcon((s, e) => OnExit());

		if (devMode) {
			TrayIconManager.SetDevModeText(trayIcon);
		}

		_ = Task.Run(async () => await RunMonitoringLoop(args));
		Application.Run();
	}

	private static void OnExit() {
		isRunning = false;
		trayIcon.Visible = false;
		Application.Exit();
		Environment.Exit(0);
	}

	static async Task RunMonitoringLoop(string[] args) {
		try {
			var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
			bool devMode = Array.Exists(args, arg => arg == AppConstants.ARG_DEV_MODE);
			bool isStartup = Array.Exists(args, arg => arg == AppConstants.ARG_STARTUP);

			Logger.Info($"Version: {version}");
			Logger.Info($"Running from: {AppInstaller.GetCurrentExecutablePath()}");
			if (devMode) {
				Logger.Info("DEV MODE: Install check skipped");
			}

			string githubToken = null;

			for (int i = 0; i < args.Length; i++) {
				if (args[i] == AppConstants.ARG_GH_TOKEN && i + 1 < args.Length) {
					githubToken = args[i + 1];
					Logger.Info("GitHub token provided");
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

			AudioDeviceType currentDeviceType = AudioDeviceType.None;
			audioManager = null;
			bool wasDiscordRunning = false;
			bool hasInitialized = false;
			bool wasFullscreen = false;

			while (isRunning) {
				try {
					AudioDeviceType detectedType = AudioDeviceDetector.DetectDefaultDevice(verbose: false);
					int discordInstanceCount = DiscordDetector.GetInstanceCount();
					bool isDiscordRunning = discordInstanceCount > 0;

					if (detectedType != currentDeviceType) {
						AudioDeviceDetector.DetectDefaultDevice(verbose: true);
						Logger.Info($"Default device changed from {currentDeviceType} to {detectedType}");
						if (audioManager != null) {
							audioManager.RestoreSettings();
							audioManager.Dispose();
							audioManager = null;
						}
						currentDeviceType = detectedType;
						hasInitialized = false;

						if (currentDeviceType != AudioDeviceType.None) {
							audioManager = AudioManagerFactory.CreateManager(currentDeviceType);
							if (audioManager != null) {
								Logger.Info("Audio device manager initialized successfully");
								if (isDiscordRunning) {
									audioManager.InitializeAndSaveConfigs();
									hasInitialized = true;
								}
							}
						}
					}

					if (audioManager == null && currentDeviceType != AudioDeviceType.None) {
						audioManager = AudioManagerFactory.CreateManager(currentDeviceType);
						if (audioManager != null) {
							Logger.Info("Audio device manager initialized successfully");
						}
					}

					if (audioManager == null) {
						Thread.Sleep(AppConstants.MONITORING_INTERVAL_MS);
						continue;
					}

					bool isFullscreen = FullscreenDetector.IsFullscreenAppActive();

					if (isFullscreen) {
						wasFullscreen = true;
						Thread.Sleep(AppConstants.FULLSCREEN_SKIP_INTERVAL_MS);
						continue;
					}

					if (wasFullscreen && !isFullscreen) {
						wasFullscreen = false;
					}

					if (isDiscordRunning != wasDiscordRunning) {
						if (isDiscordRunning) {
							if (!hasInitialized) {
								if (isStartup) {
									audioManager.InitializeAndSaveConfigs();
								}
								hasInitialized = true;
							}
						} else {
							Logger.Info("Discord stopped");
						}
						wasDiscordRunning = isDiscordRunning;
					} else if (isDiscordRunning) {
						audioManager.ApplyMuteSettings();
					}

					Thread.Sleep(AppConstants.MONITORING_INTERVAL_MS);
				} catch (Exception ex) {
					Logger.Error($"Error in monitoring loop: {ex.Message}");
					Thread.Sleep(AppConstants.ERROR_RETRY_INTERVAL_MS);
				}
			}
		} catch (Exception ex) {
			Logger.Error($"Fatal error: {ex.Message}");
		}
	}

}