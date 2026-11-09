//! Find running Discord processes.

use std::collections::BTreeMap;
use std::path::Path;

use windows::Win32::Foundation::CloseHandle;
use windows::Win32::System::Diagnostics::ToolHelp::{
	CreateToolhelp32Snapshot, PROCESSENTRY32W, Process32FirstW, Process32NextW, TH32CS_SNAPPROCESS,
};

/// Process names without `.exe`, as the C# version matched them.
pub const PROCESS_NAMES: [&str; 3] = ["Discord", "DiscordPTB", "DiscordDevelopment"];

/// Canonical Discord process name for an executable file name, if it is one.
pub fn discord_name(exe_file: &str) -> Option<&'static str> {
	let stem = Path::new(exe_file).file_stem()?.to_str()?;
	PROCESS_NAMES
		.into_iter()
		.find(|name| name.eq_ignore_ascii_case(stem))
}

/// Running Discord processes: PID to canonical process name.
pub fn running_instances() -> BTreeMap<u32, &'static str> {
	let mut instances = BTreeMap::new();

	unsafe {
		let Ok(snapshot) = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0) else {
			return instances;
		};

		let mut entry = PROCESSENTRY32W {
			dwSize: size_of::<PROCESSENTRY32W>() as u32,
			..Default::default()
		};
		let mut more = Process32FirstW(snapshot, &mut entry).is_ok();

		while more {
			let len = entry
				.szExeFile
				.iter()
				.position(|&c| c == 0)
				.unwrap_or(entry.szExeFile.len());
			let exe = String::from_utf16_lossy(&entry.szExeFile[..len]);
			if let Some(name) = discord_name(&exe) {
				instances.insert(entry.th32ProcessID, name);
			}
			more = Process32NextW(snapshot, &mut entry).is_ok();
		}

		let _ = CloseHandle(snapshot);
	}

	instances
}

#[cfg(test)]
mod tests {
	use super::*;

	#[test]
	fn matches_discord_variants() {
		assert_eq!(discord_name("Discord.exe"), Some("Discord"));
		assert_eq!(discord_name("discordptb.EXE"), Some("DiscordPTB"));
		assert_eq!(
			discord_name("DiscordDevelopment.exe"),
			Some("DiscordDevelopment")
		);
	}

	#[test]
	fn rejects_others() {
		assert_eq!(discord_name("DiscordHelper.exe"), None);
		assert_eq!(discord_name("Update.exe"), None);
	}
}
