using Mutelith;
using System;
using System.Threading;
using System.Threading.Tasks;

class Program {
	static async Task Main(string[] args) {
		var assembly = System.Reflection.Assembly.GetExecutingAssembly();
		var version = assembly.GetName().Version;

		Logger.Info($"Version: {version}");
		Console.WriteLine();

		string githubToken = null;

		for (int i = 0; i < args.Length; i++) {
			if (args[i] == "--ghtoken" && i + 1 < args.Length) {
				githubToken = args[i + 1];
				Logger.Info("GitHub token provided");
				break;
			}
		}

		var updateChecker = new UpdateChecker(githubToken);
		bool shouldExit = await updateChecker.CheckForUpdatesAsync();

		if (shouldExit) {
			return;
		}

		Console.WriteLine();

		var audioManager = new SonarManager();
		bool wasDiscordRunning = false;
		bool hasInitialized = false;

		while (true) {
			try {
				bool isDiscordRunning = DiscordDetector.IsRunning();

				if (isDiscordRunning != wasDiscordRunning) {
					if (isDiscordRunning) {
						int instanceCount = DiscordDetector.GetInstanceCount();
						Logger.Success($"Discord started ({instanceCount} instance{(instanceCount > 1 ? "s" : "")})");
						Console.WriteLine();

						if (!hasInitialized) {
							audioManager.InitializeAndSaveConfigs();
							hasInitialized = true;
						}
						else {
							audioManager.ApplyMuteSettings();
						}
					}
					else {
						Logger.Warning("Discord stopped - restoring settings");
						Console.WriteLine();
					}
					wasDiscordRunning = isDiscordRunning;
				}
				else if (isDiscordRunning) {
					audioManager.ApplyMuteSettings();
				}

				Thread.Sleep(2000);
			} catch (Exception ex) {
				Logger.Error($"Error in monitoring loop: {ex.Message}");
				Thread.Sleep(5000);
			}
		}
	}
}