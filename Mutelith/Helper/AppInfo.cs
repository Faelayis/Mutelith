using System.IO;
using System.Reflection;

namespace Mutelith {
	public static class AppInfo {
		public static string GetDisplayVersion() {
			var assembly = typeof(AppInfo).Assembly;

			var fileVersionAttr = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
			if (!string.IsNullOrEmpty(fileVersionAttr?.Version)) {
				return fileVersionAttr.Version;
			}

			var asmVersion = assembly.GetName().Version;
			if (asmVersion != null) {
				return asmVersion.ToString();
			}

			return "Unknown";
		}

		public static long GetDirectorySize(string folderPath) {
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
	}
}