using Octokit;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

namespace Mutelith {
	public class UpdateChecker {
		private readonly string _githubOwner;
		private readonly string _githubRepo;
		private readonly GitHubClient _client;
		private readonly Version _currentVersion;
		public UpdateChecker() : this(null) {
		}
		public UpdateChecker(string githubToken) {
			var assembly = Assembly.GetExecutingAssembly();
			var repoUrl = GetRepositoryUrl(assembly);

			if (string.IsNullOrEmpty(repoUrl)) {
				_githubOwner = "Faelayis";
				_githubRepo = "Mutelith";
			}
			else {
				ParseRepositoryUrl(repoUrl, out _githubOwner, out _githubRepo);
			}

			_client = new GitHubClient(new ProductHeaderValue(_githubRepo));

			string tokenToUse = githubToken;

			if (!string.IsNullOrEmpty(tokenToUse)) {
				_client.Credentials = new Credentials(tokenToUse);
			}

			_currentVersion = assembly.GetName().Version;
		}

		private string GetRepositoryUrl(Assembly assembly) {
			try {
				var attributes = assembly.GetCustomAttributes<AssemblyMetadataAttribute>();
				var repoUrlAttribute = attributes.FirstOrDefault(a => a.Key == "RepositoryUrl");
				return repoUrlAttribute?.Value ?? "";
			} catch {
				return "";
			}
		}

		private void ParseRepositoryUrl(string repositoryUrl, out string owner, out string repo) {
			try {
				var uri = new Uri(repositoryUrl);
				var segments = uri.AbsolutePath.Trim('/').Split('/');

				if (segments.Length >= 2) {
					owner = segments[0];
					repo = segments[1];
					return;
				}
			} catch {
			}

			owner = "Faelayis";
			repo = "Mutelith";
		}

		public async Task<bool> CheckForUpdatesAsync() {
			try {
				Logger.Info("Checking for updates...");

				var releases = await _client.Repository.Release.GetAll(_githubOwner, _githubRepo);
				var latestRelease = releases.FirstOrDefault(r => !r.Prerelease);

				if (latestRelease == null) {
					Logger.Info("No releases found");
					return false;
				}

				string versionString = latestRelease.TagName.TrimStart('v');
				if (!Version.TryParse(versionString, out Version latestVersion)) {
					Logger.Warning($"Could not parse version from tag: {latestRelease.TagName}");
					return false;
				}

				Logger.Info($"Current version: {_currentVersion}");
				Logger.Info($"Latest version: {latestVersion}");

				if (latestVersion > _currentVersion) {
					Logger.Success($"New version available: {latestVersion}");
					Console.WriteLine($"Release notes: {latestRelease.Body}");
					Console.WriteLine();
					Logger.Info("Auto-updating to latest version...");
					await DownloadAndInstallUpdateAsync(latestRelease);

					return true;
				}

				return false;
			} catch (Exception ex) {
				Logger.Error($"Error checking for updates: {ex.Message}");
				return false;
			}
		}

		private async Task DownloadAndInstallUpdateAsync(Release release) {
			try {
				Logger.Info($"Release has {release.Assets.Count} assets:");
				foreach (var availableAsset in release.Assets) {
					Logger.Info($"  - Name: {availableAsset.Name}");
					Logger.Info($"    Size: {availableAsset.Size} bytes");
					Logger.Info($"    URL: {availableAsset.BrowserDownloadUrl}");
					Logger.Info($"    Content Type: {availableAsset.ContentType}");
					Console.WriteLine();
				}

				var asset = release.Assets.FirstOrDefault(a =>
					a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
					a.Name.Contains("win", StringComparison.OrdinalIgnoreCase));

				if (asset == null) {
					Logger.Warning("No Windows executable found in release assets");
					Logger.Info("Looking for any .exe file...");
					asset = release.Assets.FirstOrDefault(a =>
					  a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
				}

				if (asset == null) {
					Logger.Error("No executable file found in release assets");
					Logger.Error("Available file extensions:");
					foreach (var a in release.Assets) {
						Logger.Error($"  - {Path.GetExtension(a.Name)}");
					}
					return;
				}

				Logger.Info($"Selected asset: {asset.Name}");
				Logger.Info($"Asset size: {asset.Size} bytes");
				Logger.Info($"Download URL: {asset.BrowserDownloadUrl}");

				string tempPath = Path.Combine(Path.GetTempPath(), asset.Name);

				using (var httpClient = new HttpClient()) {
					httpClient.DefaultRequestHeaders.Add("User-Agent", "Mutelith");
					httpClient.DefaultRequestHeaders.Add("Accept", "application/octet-stream");

					if (_client.Credentials != null && !string.IsNullOrEmpty(_client.Credentials.GetToken())) {
						httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_client.Credentials.GetToken()}");
					}

					Logger.Info("Downloading via GitHub releases API...");

					string apiUrl = $"https://api.github.com/repos/{_githubOwner}/{_githubRepo}/releases/assets/{asset.Id}";
					Logger.Info($"API URL: {apiUrl}");

					var response = await httpClient.GetAsync(apiUrl);

					if (!response.IsSuccessStatusCode) {
						Logger.Warning($"API download failed: {response.StatusCode} - {response.ReasonPhrase}");
						Logger.Info("Trying direct browser download URL...");

						response = await httpClient.GetAsync(asset.BrowserDownloadUrl);

						if (!response.IsSuccessStatusCode) {
							Logger.Error($"HTTP Error: {response.StatusCode} - {response.ReasonPhrase}");
							Logger.Error($"Response content: {await response.Content.ReadAsStringAsync()}");
							return;
						}
					}

					await using var fileStream = File.Create(tempPath);
					await response.Content.CopyToAsync(fileStream);
					Logger.Success("Download completed successfully");
				}

				Logger.Success($"Downloaded to: {tempPath}");
				Logger.Info("Starting update installation...");

				string currentExePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
				string updateBatchPath = Path.Combine(Path.GetTempPath(), "update.bat");
				string batchContent = $@"@echo off
timeout /t 2 /nobreak > nul
copy /y ""{tempPath}"" ""{currentExePath}""
del ""{tempPath}""
start """" ""{currentExePath}""
del ""%~f0""
";

				File.WriteAllText(updateBatchPath, batchContent);
				Process.Start(new ProcessStartInfo {
					FileName = updateBatchPath,
					CreateNoWindow = true,
					UseShellExecute = false
				});

				Logger.Success("Update will be installed after the application exits");
				Environment.Exit(0);
			} catch (Exception ex) {
				Logger.Error($"Error downloading/installing update: {ex.Message}");
			}
		}
	}
}