//! Mutelith tray app: keeps Discord muted on the devices that cause echo for
//! viewers while screen sharing, and restores it on exit.

// Release builds have no console window; debug builds keep one for logs.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod args;
mod install;
mod instance;
mod logger;
mod theme;
mod tray;
mod update;

use std::collections::HashSet;
use std::sync::Mutex;
use std::sync::atomic::{AtomicBool, Ordering};
use std::thread::{self, JoinHandle};

use mutelith_core::audio::{ComGuard, DeviceEnumerator};
use mutelith_core::monitor::{Monitor, RESUME_SETTLE_DELAY, sleep_unless};
use mutelith_core::{discord, store, viewer_echo};
use windows::Win32::System::Console::{ATTACH_PARENT_PROCESS, AllocConsole, AttachConsole};
use windows::Win32::UI::WindowsAndMessaging::{
	MB_ICONERROR, MB_OK, MESSAGEBOX_RESULT, MESSAGEBOX_STYLE, MessageBoxW,
};
use windows::core::{HSTRING, w};

use crate::args::Args;

const VERSION: &str = env!("CARGO_PKG_VERSION");

static STOP: AtomicBool = AtomicBool::new(false);
static RESUMED: AtomicBool = AtomicBool::new(false);
static WORKER: Mutex<Option<JoinHandle<()>>> = Mutex::new(None);

fn main() {
	let args = Args::parse(std::env::args().skip(1));
	if args.dev {
		attach_console();
	}
	logger::init(args.dev, args.logs, install::log_path());

	log::info!("Version: {VERSION}");
	log::info!("Running from: {}", install::current_exe().display());

	instance::close_previous_instances(args.dev);

	if args.uninstall {
		release_leftovers();
		install::uninstall();
		return;
	}

	if args.dev {
		log::info!("DEV MODE: Install check skipped");
	} else if !install::is_installed() {
		match install::install_and_restart() {
			Ok(()) => return,
			Err(err) => {
				log::error!("Installation failed: {err}");
				show_error(&format!(
					"Failed to install Mutelith:\n{err}\n\nPlease check permissions and try again."
				));
			}
		}
	} else if let Err(err) = install::register() {
		log::error!("Failed to refresh Programs and Features entry: {err}");
	}

	update::init(!args.dev && install::is_installed(), args.gh_token.clone());
	*WORKER.lock().unwrap() = Some(thread::spawn(worker));

	let tip = if args.dev {
		format!("Mutelith v{VERSION} (DEV)")
	} else {
		format!("Mutelith v{VERSION}")
	};
	if let Err(err) = tray::run(tip) {
		log::error!("Tray icon failed: {err}");
	}

	stop_worker();
}

/// Stop the monitoring thread and wait until it has restored Discord. Safe to call twice.
pub fn stop_worker() {
	STOP.store(true, Ordering::Relaxed);
	let handle = WORKER.lock().unwrap().take();
	if let Some(handle) = handle {
		let _ = handle.join();
	}
}

/// Called by the tray window when the system resumes from sleep or hibernate.
pub fn on_resume() {
	log::info!("System resumed from sleep/hibernate. Flagging for re-initialization...");
	RESUMED.store(true, Ordering::Relaxed);
}

fn worker() {
	let _com = match ComGuard::init() {
		Ok(com) => com,
		Err(err) => {
			log::error!("Fatal error: COM init failed: {err}");
			return;
		}
	};

	if update::on_startup() {
		tray::request_quit();
		return;
	}

	let mut monitor = match Monitor::new() {
		Ok(monitor) => monitor,
		Err(err) => {
			log::error!("Fatal error: {err}");
			return;
		}
	};

	while !STOP.load(Ordering::Relaxed) {
		if RESUMED.swap(false, Ordering::Relaxed) {
			log::info!("Processing system resume. Waiting for audio devices to stabilize...");
			sleep_unless(&[&STOP], RESUME_SETTLE_DELAY);
			monitor.reset();
			continue;
		}

		let wait = monitor.tick();
		sleep_unless(&[&STOP, &RESUMED, monitor.wake_flag()], wait);
	}

	monitor.restore();
	log::info!("Stopped monitoring");
}

/// Unmute Discord on the devices where Mutelith left it muted, then forget them.
fn release_leftovers() {
	let Ok(_com) = ComGuard::init() else {
		return;
	};
	if let Ok(enumerator) = DeviceEnumerator::new() {
		let instances = discord::running_instances();
		viewer_echo::release_leftovers(&enumerator, &HashSet::new(), &instances);
	}
	store::clear();
}

fn attach_console() {
	unsafe {
		if AttachConsole(ATTACH_PARENT_PROCESS).is_err() {
			let _ = AllocConsole();
		}
	}
}

fn show_error(text: &str) {
	message(text, MB_OK | MB_ICONERROR);
}

fn message(text: &str, style: MESSAGEBOX_STYLE) -> MESSAGEBOX_RESULT {
	unsafe { MessageBoxW(None, &HSTRING::from(text), w!("Mutelith"), style) }
}
