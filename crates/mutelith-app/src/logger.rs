//! Log to a console in dev mode, and to the log file when `--logs` is given.
//! Errors always go to the log file.

use std::fs::{self, File, OpenOptions};
use std::io::Write;
use std::path::PathBuf;
use std::sync::{Mutex, OnceLock};

use log::{Level, LevelFilter, Metadata, Record};
use windows::Win32::System::SystemInformation::GetLocalTime;

struct AppLogger {
	console: bool,
	file: bool,
	path: PathBuf,
	write_lock: Mutex<()>,
}

static LOGGER: OnceLock<AppLogger> = OnceLock::new();

/// The log file is emptied at startup once it grows past this size.
const MAX_LOG_BYTES: u64 = 1024 * 1024;

pub fn init(console: bool, file: bool, path: PathBuf) {
	if fs::metadata(&path).is_ok_and(|meta| meta.len() > MAX_LOG_BYTES) {
		let _ = File::create(&path);
	}

	let logger = LOGGER.get_or_init(|| AppLogger {
		console,
		file,
		path,
		write_lock: Mutex::new(()),
	});
	if log::set_logger(logger).is_ok() {
		log::set_max_level(LevelFilter::Info);
	}
}

impl log::Log for AppLogger {
	fn enabled(&self, metadata: &Metadata) -> bool {
		metadata.level() <= Level::Info
	}

	fn log(&self, record: &Record) {
		if !self.enabled(record.metadata()) {
			return;
		}

		let t = unsafe { GetLocalTime() };
		let line = format!(
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

		if self.console {
			eprintln!("{line}");
		}

		if self.file || record.level() == Level::Error {
			let _guard = self.write_lock.lock();
			if let Some(dir) = self.path.parent() {
				let _ = fs::create_dir_all(dir);
			}
			if let Ok(mut file) = OpenOptions::new()
				.create(true)
				.append(true)
				.open(&self.path)
			{
				let _ = writeln!(file, "{line}");
			}
		}
	}

	fn flush(&self) {}
}
