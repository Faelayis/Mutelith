//! Self-update from GitHub releases: on startup (silent) and from the tray
//! menu "Check for Updates..." (asks first and reports the result).

use std::sync::OnceLock;
use std::sync::atomic::{AtomicBool, Ordering};
use std::thread;

use mutelith_update::{ASSET_NAME, REPOSITORY, Update, Updater, Version};
use windows::Win32::UI::WindowsAndMessaging::{
	IDYES, MB_ICONINFORMATION, MB_ICONQUESTION, MB_OK, MB_YESNO,
};

use crate::{VERSION, install, message, show_error, tray};

/// Set once at startup: whether this copy may update itself (installed, not `--dev`).
static CAN_UPDATE: AtomicBool = AtomicBool::new(false);
/// True while an update check runs, so the startup check and the menu never overlap.
static UPDATING: AtomicBool = AtomicBool::new(false);
static GH_TOKEN: OnceLock<Option<String>> = OnceLock::new();

/// Call once at startup, before the worker or the tray can check for updates.
pub fn init(can_update: bool, token: Option<String>) {
	CAN_UPDATE.store(can_update, Ordering::Relaxed);
	let _ = GH_TOKEN.set(token);
}

/// Whether the tray shows "Check for Updates...".
pub fn can_update() -> bool {
	CAN_UPDATE.load(Ordering::Relaxed)
}

/// Startup check: install a newer release without asking. Returns true when
/// the new version was started and this process should exit.
pub fn on_startup() -> bool {
	if !can_update() || UPDATING.swap(true, Ordering::Relaxed) {
		return false;
	}
	let restarting = startup_update();
	UPDATING.store(false, Ordering::Relaxed);
	restarting
}

/// Tray menu "Check for Updates...": check on a background thread and report
/// the result in a message box. Ignored while another check runs.
pub fn check_from_menu() {
	if UPDATING.swap(true, Ordering::Relaxed) {
		return;
	}
	thread::spawn(|| {
		let restarting = menu_update();
		UPDATING.store(false, Ordering::Relaxed);
		if restarting {
			tray::request_quit();
		}
	});
}

fn updater() -> Updater {
	let current = Version::parse(VERSION).expect("crate version is X.Y.Z");
	let token = GH_TOKEN.get().cloned().flatten();
	Updater::new(REPOSITORY, ASSET_NAME, current).with_token(token)
}

fn startup_update() -> bool {
	log::info!("Checking for updates...");
	let updater = updater();
	match updater.check() {
		Ok(Some(update)) => {
			log::info!(
				"New version available: {}. Auto-updating...",
				update.version
			);
			install_and_relaunch(&updater, &update).is_ok()
		}
		Ok(None) => {
			log::info!("No updates available");
			false
		}
		Err(err) => {
			log::warn!("Error checking for updates: {err}");
			false
		}
	}
}

/// Like [`startup_update`], but asks before installing and always tells the user the result.
fn menu_update() -> bool {
	log::info!("Checking for updates (from menu)...");
	let updater = updater();
	let update = match updater.check() {
		Ok(Some(update)) => update,
		Ok(None) => {
			log::info!("No updates available");
			message(
				&format!("Mutelith v{VERSION} is the latest version."),
				MB_OK | MB_ICONINFORMATION,
			);
			return false;
		}
		Err(err) => {
			log::warn!("Error checking for updates: {err}");
			show_error(&format!("Could not check for updates:\n{err}"));
			return false;
		}
	};

	let question = format!(
		"Mutelith v{} is available. You have v{VERSION}.\n\nInstall it now? Mutelith restarts, and Discord is unmuted for a moment.",
		update.version
	);
	if message(&question, MB_YESNO | MB_ICONQUESTION) != IDYES {
		log::info!("Update to {} declined", update.version);
		return false;
	}

	match install_and_relaunch(&updater, &update) {
		Ok(()) => true,
		Err(err) => {
			show_error(&format!("Could not install the update:\n{err}"));
			false
		}
	}
}

/// Replace the installed exe with `update` and start it. The caller then exits.
fn install_and_relaunch(updater: &Updater, update: &Update) -> Result<(), String> {
	if let Err(err) = updater.install(update) {
		log::error!("Error installing update: {err}");
		return Err(err.to_string());
	}

	match mutelith_update::relaunch(&install::install_path(), &install::STARTUP_ARGS) {
		Ok(()) => {
			log::info!("Updated to {}. Restarting...", update.version);
			Ok(())
		}
		Err(err) => {
			log::error!("Failed to start the updated version: {err}");
			Err(err.to_string())
		}
	}
}
