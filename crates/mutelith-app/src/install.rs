//! Install to `%LOCALAPPDATA%\Mutelith`, start with Windows, appear in
//! Programs and Features, and uninstall. Paths and registry names match the
//! C# app, so an update overwrites its entries instead of adding new ones.

use std::fs;
use std::io;
use std::os::windows::process::CommandExt;
use std::path::{Path, PathBuf};
use std::process::Command;

use winreg::RegKey;
use winreg::enums::{HKEY_CURRENT_USER, KEY_SET_VALUE};

pub const APP_NAME: &str = "Mutelith";
pub const EXE_NAME: &str = "Mutelith.exe";
pub const PUBLISHER: &str = "Faelayis";
pub const REPOSITORY_URL: &str = "https://github.com/Faelayis/Mutelith";
/// Arguments for the startup entry and for restarts after install or update.
pub const STARTUP_ARGS: [&str; 2] = ["--startup", "--silent"];

const RUN_KEY: &str = r"Software\Microsoft\Windows\CurrentVersion\Run";
const UNINSTALL_KEY: &str = r"Software\Microsoft\Windows\CurrentVersion\Uninstall\Mutelith";
const CREATE_NO_WINDOW: u32 = 0x0800_0000;

pub fn install_dir() -> PathBuf {
	std::env::var_os("LOCALAPPDATA")
		.map(PathBuf::from)
		.unwrap_or_else(std::env::temp_dir)
		.join(APP_NAME)
}

pub fn install_path() -> PathBuf {
	install_dir().join(EXE_NAME)
}

pub fn log_path() -> PathBuf {
	install_dir().join("logs.txt")
}

pub fn current_exe() -> PathBuf {
	std::env::current_exe().unwrap_or_else(|_| PathBuf::from(EXE_NAME))
}

pub fn is_installed() -> bool {
	same_path(&current_exe(), &install_path())
}

/// Copy this exe to the install folder, register it, and start the installed copy.
pub fn install_and_restart() -> io::Result<()> {
	let (from, to) = (current_exe(), install_path());
	log::info!("Installing from: {}", from.display());
	log::info!("Installing to: {}", to.display());

	fs::create_dir_all(install_dir())?;
	fs::copy(&from, &to)?;
	log::info!("File copied successfully");

	let command = format!("\"{}\" {}", to.display(), STARTUP_ARGS.join(" "));
	let (run, _) = RegKey::predef(HKEY_CURRENT_USER).create_subkey(RUN_KEY)?;
	run.set_value(APP_NAME, &command)?;
	log::info!("Added to startup registry with {}", STARTUP_ARGS.join(" "));

	register()?;

	Command::new(&to).args(STARTUP_ARGS).spawn()?;
	log::info!("Started new instance from install path");
	Ok(())
}

/// Write or refresh the Programs and Features entry.
pub fn register() -> io::Result<()> {
	let (key, _) = RegKey::predef(HKEY_CURRENT_USER).create_subkey(UNINSTALL_KEY)?;
	let exe = install_path();
	let size_kb = u32::try_from(dir_size(&install_dir()) / 1024)
		.unwrap_or(u32::MAX)
		.max(1);

	key.set_value("DisplayName", &APP_NAME)?;
	key.set_value("DisplayVersion", &env!("CARGO_PKG_VERSION"))?;
	key.set_value("Publisher", &PUBLISHER)?;
	key.set_value("InstallLocation", &install_dir().display().to_string())?;
	key.set_value("DisplayIcon", &exe.display().to_string())?;
	key.set_value(
		"UninstallString",
		&format!("\"{}\" --uninstall", exe.display()),
	)?;
	key.set_value("NoModify", &1u32)?;
	key.set_value("NoRepair", &1u32)?;
	key.set_value("HelpLink", &format!("{REPOSITORY_URL}/issues"))?;
	key.set_value("URLInfoAbout", &REPOSITORY_URL)?;
	key.set_value("EstimatedSize", &size_kb)?;

	log::info!("Registered app in Programs and Features");
	Ok(())
}

/// Remove the registry entries, then delete the install folder after this process exits.
pub fn uninstall() {
	let hkcu = RegKey::predef(HKEY_CURRENT_USER);
	match hkcu.delete_subkey_all(UNINSTALL_KEY) {
		Ok(()) => log::info!("Removed Programs and Features entry"),
		Err(err) if err.kind() == io::ErrorKind::NotFound => {}
		Err(err) => log::error!("Failed to remove Programs and Features entry: {err}"),
	}
	if let Ok(run) = hkcu.open_subkey_with_flags(RUN_KEY, KEY_SET_VALUE) {
		let _ = run.delete_value(APP_NAME);
	}
	log::info!("Unregistered app from Programs and Features and Startup");

	let dir = install_dir();
	if !dir.is_dir() {
		log::info!("Install folder does not exist, skipping folder delete.");
		return;
	}

	// The running exe cannot delete its own folder, so a hidden cmd.exe waits
	// about 3 seconds for this process to exit, then removes the folder.
	let script = format!(
		"/C ping -n 4 127.0.0.1 >nul & rmdir /s /q \"{}\"",
		dir.display()
	);
	match Command::new("cmd.exe")
		.raw_arg(script)
		.current_dir(std::env::temp_dir())
		.creation_flags(CREATE_NO_WINDOW)
		.spawn()
	{
		Ok(_) => log::info!("Scheduled install folder deletion: {}", dir.display()),
		Err(err) => log::error!("Failed to schedule install folder deletion: {err}"),
	}
}

pub fn same_path(a: &Path, b: &Path) -> bool {
	a.to_string_lossy()
		.eq_ignore_ascii_case(&b.to_string_lossy())
}

fn dir_size(dir: &Path) -> u64 {
	fs::read_dir(dir)
		.into_iter()
		.flatten()
		.flatten()
		.filter_map(|entry| entry.metadata().ok())
		.filter(|meta| meta.is_file())
		.map(|meta| meta.len())
		.sum()
}

#[cfg(test)]
mod tests {
	use super::*;

	#[test]
	fn install_path_is_under_local_app_data() {
		let path = install_path();
		assert!(path.ends_with(r"Mutelith\Mutelith.exe"));
	}

	#[test]
	fn same_path_ignores_case() {
		assert!(same_path(
			Path::new(r"C:\Users\A\AppData\Local\Mutelith\Mutelith.exe"),
			Path::new(r"c:\users\a\appdata\local\mutelith\MUTELITH.EXE")
		));
		assert!(!same_path(
			Path::new(r"C:\Downloads\Mutelith.exe"),
			Path::new(r"C:\Users\A\AppData\Local\Mutelith\Mutelith.exe")
		));
	}
}
