//! Test harness for mutelith-core on a real machine.
//!
//! `mutelith-cli detect` prints the default device and the detected provider.
//! `mutelith-cli watch` runs the monitoring loop until Enter is pressed, then restores.
//! `mutelith-cli update [--current X.Y.Z] [--asset NAME] [--download]` checks GitHub
//! for a newer release; with `--download` it also downloads and verifies it, but never installs.

use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};
use std::thread;

use log::{Level, LevelFilter, Metadata, Record};
use mutelith_core::audio::{ComGuard, DeviceEnumerator};
use mutelith_core::discord;
use mutelith_core::monitor::{Monitor, sleep_unless};
use mutelith_core::provider::Provider;
use mutelith_update::{ASSET_NAME, REPOSITORY, Updater, Version};
use windows::Win32::System::SystemInformation::GetLocalTime;

struct ConsoleLogger;

impl log::Log for ConsoleLogger {
	fn enabled(&self, metadata: &Metadata) -> bool {
		metadata.level() <= Level::Info
	}

	fn log(&self, record: &Record) {
		if self.enabled(record.metadata()) {
			let t = unsafe { GetLocalTime() };
			eprintln!(
				"[{:04}-{:02}-{:02} {:02}:{:02}:{:02}] {:<5} {}",
				t.wYear,
				t.wMonth,
				t.wDay,
				t.wHour,
				t.wMinute,
				t.wSecond,
				record.level(),
				record.args()
			);
		}
	}

	fn flush(&self) {}
}

fn main() -> Result<(), mutelith_update::Error> {
	log::set_logger(&ConsoleLogger).expect("logger already set");
	log::set_max_level(LevelFilter::Info);
	let _com = ComGuard::init()?;

	let args: Vec<String> = std::env::args().skip(1).collect();
	match args.first().map(String::as_str) {
		Some("detect") => Ok(detect()?),
		Some("watch") | None => Ok(watch()?),
		Some("update") => update(&args[1..]),
		Some(other) => {
			eprintln!("unknown command: {other}\nusage: mutelith-cli [detect|watch|update]");
			std::process::exit(2);
		}
	}
}

fn update(args: &[String]) -> Result<(), mutelith_update::Error> {
	let mut current = env!("CARGO_PKG_VERSION").to_string();
	let mut asset = ASSET_NAME.to_string();
	let mut download = false;

	let mut options = args.iter();
	while let Some(option) = options.next() {
		match option.as_str() {
			"--current" => current = options.next().ok_or("--current needs a version")?.clone(),
			"--asset" => asset = options.next().ok_or("--asset needs a file name")?.clone(),
			"--download" => download = true,
			other => return Err(format!("unknown update option: {other}").into()),
		}
	}

	let current = Version::parse(&current).ok_or("--current must look like 1.2.3")?;
	let updater =
		Updater::new(REPOSITORY, asset, current).with_token(std::env::var("GITHUB_TOKEN").ok());

	log::info!("Current version: {current}");
	let Some(update) = updater.check()? else {
		log::info!("No updates available");
		return Ok(());
	};
	log::info!(
		"New version available: {} ({}, {} bytes)",
		update.version,
		update.asset.name,
		update.asset.size
	);

	if download {
		let path = updater.download(&update)?;
		log::info!("Downloaded and verified SHA-256: {}", path.display());
	}
	Ok(())
}

fn detect() -> windows::core::Result<()> {
	let enumerator = DeviceEnumerator::new()?;

	let default = enumerator.default_render_device()?;
	let (name, controller) = (default.friendly_name(), default.controller_info());
	println!("Default device: '{name}'");
	println!("Controller:     '{controller}'");
	println!("Provider:       {:?}", Provider::detect(&name, &controller));

	println!("\nActive output devices:");
	for device in enumerator.active_render_devices()? {
		println!("  - {}", device.friendly_name());
	}

	println!("\nDiscord processes:");
	for (pid, name) in discord::running_instances() {
		println!("  - {name} (PID: {pid})");
	}

	Ok(())
}

fn watch() -> windows::core::Result<()> {
	let stop = Arc::new(AtomicBool::new(false));
	{
		let stop = Arc::clone(&stop);
		thread::spawn(move || {
			let _ = std::io::stdin().read_line(&mut String::new());
			stop.store(true, Ordering::Relaxed);
		});
	}
	log::info!("Watching. Press Enter to stop and restore.");

	let mut monitor = Monitor::new()?;
	while !stop.load(Ordering::Relaxed) {
		let wait = monitor.tick();
		sleep_unless(&[&stop, monitor.wake_flag()], wait);
	}
	monitor.restore();
	Ok(())
}
