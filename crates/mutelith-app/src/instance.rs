//! Keep a single running instance: close other Mutelith processes on startup,
//! including the C# app, so a new install or update can replace the exe.

use std::path::PathBuf;

use windows::Win32::Foundation::{CloseHandle, HANDLE, LPARAM, WAIT_OBJECT_0, WPARAM};
use windows::Win32::System::Diagnostics::ToolHelp::{
	CreateToolhelp32Snapshot, PROCESSENTRY32W, Process32FirstW, Process32NextW, TH32CS_SNAPPROCESS,
};
use windows::Win32::System::Threading::{
	OpenProcess, PROCESS_NAME_WIN32, PROCESS_QUERY_LIMITED_INFORMATION, PROCESS_SYNCHRONIZE,
	PROCESS_TERMINATE, QueryFullProcessImageNameW, TerminateProcess, WaitForSingleObject,
};
use windows::Win32::UI::WindowsAndMessaging::{
	FindWindowExW, GetWindowThreadProcessId, PostMessageW, WM_CLOSE,
};
use windows::core::{PCWSTR, PWSTR};

use crate::install::{self, EXE_NAME, same_path};
use crate::tray;

const EXIT_WAIT_MS: u32 = 5000;

/// Close other instances. In dev mode, only instances of this same exe path
/// are closed, so a dev build never stops an installed copy.
pub fn close_previous_instances(dev: bool) {
	let own_pid = std::process::id();
	let own_path = install::current_exe();
	let own_name = own_path
		.file_name()
		.map(|n| n.to_string_lossy().into_owned());

	for (pid, exe) in processes() {
		let matches_name = exe.eq_ignore_ascii_case(EXE_NAME)
			|| own_name
				.as_deref()
				.is_some_and(|own| exe.eq_ignore_ascii_case(own));
		if pid == own_pid || !matches_name {
			continue;
		}

		unsafe {
			let Ok(process) = OpenProcess(
				PROCESS_TERMINATE | PROCESS_SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION,
				false,
				pid,
			) else {
				log::error!("Failed to open previous instance (PID {pid})");
				continue;
			};

			if !dev || image_path(process).is_some_and(|path| same_path(&path, &own_path)) {
				log::info!("Found previous Mutelith instance (PID {pid}), closing it...");
				// Ask first so it can unmute Discord; the C# app has no such window.
				if post_close(pid) && WaitForSingleObject(process, EXIT_WAIT_MS) == WAIT_OBJECT_0 {
					log::info!("Previous instance {pid} exited");
				} else {
					match TerminateProcess(process, 0) {
						Ok(()) => {
							WaitForSingleObject(process, EXIT_WAIT_MS);
						}
						Err(err) => log::error!("Failed to close previous instance {pid}: {err}"),
					}
				}
			}

			let _ = CloseHandle(process);
		}
	}
}

/// Post `WM_CLOSE` to the tray window owned by `pid`. Returns whether one was found.
fn post_close(pid: u32) -> bool {
	let mut after = None;
	unsafe {
		while let Ok(hwnd) = FindWindowExW(None, after, tray::WINDOW_CLASS, PCWSTR::null()) {
			let mut owner = 0;
			GetWindowThreadProcessId(hwnd, Some(&mut owner));
			if owner == pid {
				return PostMessageW(Some(hwnd), WM_CLOSE, WPARAM(0), LPARAM(0)).is_ok();
			}
			after = Some(hwnd);
		}
	}
	false
}

/// All running processes: PID and executable file name.
fn processes() -> Vec<(u32, String)> {
	let mut list = Vec::new();

	unsafe {
		let Ok(snapshot) = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0) else {
			return list;
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
			list.push((
				entry.th32ProcessID,
				String::from_utf16_lossy(&entry.szExeFile[..len]),
			));
			more = Process32NextW(snapshot, &mut entry).is_ok();
		}

		let _ = CloseHandle(snapshot);
	}

	list
}

fn image_path(process: HANDLE) -> Option<PathBuf> {
	let mut buffer = [0u16; 1024];
	let mut len = buffer.len() as u32;
	unsafe {
		QueryFullProcessImageNameW(
			process,
			PROCESS_NAME_WIN32,
			PWSTR(buffer.as_mut_ptr()),
			&mut len,
		)
	}
	.ok()?;
	Some(String::from_utf16_lossy(&buffer[..len as usize]).into())
}
