//! Tray icon on a hidden top-level window. The window also receives the
//! power and session broadcasts that the app reacts to.

use std::sync::OnceLock;
use std::sync::atomic::{AtomicIsize, AtomicU32, Ordering};

use windows::Win32::Foundation::{HWND, LPARAM, LRESULT, POINT, WPARAM};
use windows::Win32::System::LibraryLoader::GetModuleHandleW;
use windows::Win32::UI::Shell::{
	NIF_ICON, NIF_MESSAGE, NIF_TIP, NIM_ADD, NIM_DELETE, NOTIFYICONDATAW, Shell_NotifyIconW,
	ShellExecuteW,
};
use windows::Win32::UI::WindowsAndMessaging::{
	AppendMenuW, CreatePopupMenu, CreateWindowExW, DefWindowProcW, DestroyMenu, DestroyWindow,
	DispatchMessageW, GetCursorPos, GetMessageW, IDI_APPLICATION, LoadIconW, MF_GRAYED,
	MF_SEPARATOR, MF_STRING, MSG, PBT_APMRESUMEAUTOMATIC, PostMessageW, PostQuitMessage,
	RegisterClassW, RegisterWindowMessageW, SW_SHOWNORMAL, SetForegroundWindow, TPM_NONOTIFY,
	TPM_RETURNCMD, TPM_RIGHTBUTTON, TrackPopupMenu, TranslateMessage, WINDOW_EX_STYLE, WM_APP,
	WM_CLOSE, WM_CONTEXTMENU, WM_DESTROY, WM_ENDSESSION, WM_NULL, WM_POWERBROADCAST,
	WM_QUERYENDSESSION, WM_RBUTTONUP, WM_SETTINGCHANGE, WNDCLASSW, WS_OVERLAPPED,
};
use windows::core::{Error, HSTRING, PCWSTR, Result, w};

use crate::{install, theme, update};

/// Window class of the tray window. Other instances post `WM_CLOSE` to it.
pub const WINDOW_CLASS: PCWSTR = w!("MutelithTray");

const WM_TRAY: u32 = WM_APP + 1;
const WM_QUIT_APP: u32 = WM_APP + 2;
const ICON_ID: u32 = 1;

const ID_VERSION: usize = 1;
const ID_LOGS: usize = 2;
const ID_GITHUB: usize = 3;
const ID_EXIT: usize = 4;
const ID_UPDATE: usize = 5;

static TIP: OnceLock<String> = OnceLock::new();
static WINDOW: AtomicIsize = AtomicIsize::new(0);
static TASKBAR_CREATED: AtomicU32 = AtomicU32::new(0);

/// Show the tray icon and run the message loop until the app exits.
pub fn run(tip: String) -> Result<()> {
	let _ = TIP.set(tip);
	theme::follow_system();

	unsafe {
		let instance = GetModuleHandleW(None)?;
		let class = WINDOW_CLASS;
		let window_class = WNDCLASSW {
			lpfnWndProc: Some(window_proc),
			hInstance: instance.into(),
			lpszClassName: class,
			..Default::default()
		};
		if RegisterClassW(&window_class) == 0 {
			return Err(Error::from_thread());
		}
		TASKBAR_CREATED.store(
			RegisterWindowMessageW(w!("TaskbarCreated")),
			Ordering::Relaxed,
		);

		// A hidden top-level window, not a message-only one: message-only
		// windows do not receive broadcasts such as WM_POWERBROADCAST.
		let hwnd = CreateWindowExW(
			WINDOW_EX_STYLE::default(),
			class,
			w!("Mutelith"),
			WS_OVERLAPPED,
			0,
			0,
			0,
			0,
			None,
			None,
			Some(instance.into()),
			None,
		)?;
		WINDOW.store(hwnd.0 as isize, Ordering::Relaxed);
		theme::allow_for_window(hwnd);
		add_icon(hwnd);

		let mut message = MSG::default();
		while GetMessageW(&mut message, None, 0, 0).0 > 0 {
			let _ = TranslateMessage(&message);
			DispatchMessageW(&message);
		}
	}

	WINDOW.store(0, Ordering::Relaxed);
	Ok(())
}

/// Ask the tray to exit, from any thread.
pub fn request_quit() {
	let raw = WINDOW.load(Ordering::Relaxed);
	if raw != 0 {
		unsafe {
			let _ = PostMessageW(Some(HWND(raw as _)), WM_QUIT_APP, WPARAM(0), LPARAM(0));
		}
	}
}

unsafe extern "system" fn window_proc(
	hwnd: HWND,
	message: u32,
	wparam: WPARAM,
	lparam: LPARAM,
) -> LRESULT {
	unsafe {
		match message {
			WM_TRAY => {
				let event = (lparam.0 & 0xFFFF) as u32;
				if event == WM_RBUTTONUP || event == WM_CONTEXTMENU {
					show_menu(hwnd);
				}
				LRESULT(0)
			}
			WM_POWERBROADCAST => {
				if wparam.0 as u32 == PBT_APMRESUMEAUTOMATIC {
					crate::on_resume();
				}
				LRESULT(1)
			}
			WM_SETTINGCHANGE => {
				// Sent with "ImmersiveColorSet" when the user switches light/dark mode.
				let area = PCWSTR(lparam.0 as *const u16);
				if !area.is_null() && area.to_string().is_ok_and(|s| s == "ImmersiveColorSet") {
					theme::refresh();
				}
				DefWindowProcW(hwnd, message, wparam, lparam)
			}
			WM_QUERYENDSESSION => LRESULT(1),
			WM_ENDSESSION => {
				// Windows may end the process after this returns, so restore Discord now.
				if wparam.0 != 0 {
					crate::stop_worker();
					remove_icon(hwnd);
				}
				LRESULT(0)
			}
			WM_QUIT_APP | WM_CLOSE => {
				quit(hwnd);
				LRESULT(0)
			}
			WM_DESTROY => {
				remove_icon(hwnd);
				PostQuitMessage(0);
				LRESULT(0)
			}
			_ if message != 0 && message == TASKBAR_CREATED.load(Ordering::Relaxed) => {
				// Explorer restarted: the old icon is gone.
				add_icon(hwnd);
				LRESULT(0)
			}
			_ => DefWindowProcW(hwnd, message, wparam, lparam),
		}
	}
}

/// Stop the worker (it restores Discord), then close the window.
fn quit(hwnd: HWND) {
	crate::stop_worker();
	unsafe {
		let _ = DestroyWindow(hwnd);
	}
}

fn show_menu(hwnd: HWND) {
	unsafe {
		let Ok(menu) = CreatePopupMenu() else {
			return;
		};
		let tip = HSTRING::from(TIP.get().map(String::as_str).unwrap_or("Mutelith"));
		let _ = AppendMenuW(menu, MF_STRING | MF_GRAYED, ID_VERSION, &tip);
		let _ = AppendMenuW(menu, MF_SEPARATOR, 0, PCWSTR::null());
		if update::can_update() {
			let _ = AppendMenuW(menu, MF_STRING, ID_UPDATE, w!("Check for Updates..."));
		}
		if install::log_path().is_file() {
			let _ = AppendMenuW(menu, MF_STRING, ID_LOGS, w!("Show Logs"));
		}
		let _ = AppendMenuW(menu, MF_STRING, ID_GITHUB, w!("GitHub"));
		let _ = AppendMenuW(menu, MF_SEPARATOR, 0, PCWSTR::null());
		let _ = AppendMenuW(menu, MF_STRING, ID_EXIT, w!("Exit"));

		let mut cursor = POINT::default();
		let _ = GetCursorPos(&mut cursor);
		// Without these two calls the menu does not close when clicking elsewhere.
		let _ = SetForegroundWindow(hwnd);
		let command = TrackPopupMenu(
			menu,
			TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY,
			cursor.x,
			cursor.y,
			None,
			hwnd,
			None,
		);
		let _ = PostMessageW(Some(hwnd), WM_NULL, WPARAM(0), LPARAM(0));
		let _ = DestroyMenu(menu);

		match command.0 as usize {
			ID_UPDATE => update::check_from_menu(),
			ID_LOGS => open(&install::log_path().display().to_string()),
			ID_GITHUB => open(install::REPOSITORY_URL),
			ID_EXIT => quit(hwnd),
			_ => {}
		}
	}
}

fn open(target: &str) {
	let result = unsafe {
		ShellExecuteW(
			None,
			w!("open"),
			&HSTRING::from(target),
			PCWSTR::null(),
			PCWSTR::null(),
			SW_SHOWNORMAL,
		)
	};
	// ShellExecuteW returns a value greater than 32 on success.
	if result.0 as isize <= 32 {
		log::error!("Error opening {target}");
	}
}

fn icon_data(hwnd: HWND) -> NOTIFYICONDATAW {
	NOTIFYICONDATAW {
		cbSize: size_of::<NOTIFYICONDATAW>() as u32,
		hWnd: hwnd,
		uID: ICON_ID,
		..Default::default()
	}
}

fn add_icon(hwnd: HWND) {
	let mut data = icon_data(hwnd);
	data.uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP;
	data.uCallbackMessage = WM_TRAY;
	data.hIcon = unsafe { LoadIconW(None, IDI_APPLICATION) }.unwrap_or_default();

	let tip = TIP.get().map(String::as_str).unwrap_or("Mutelith");
	let max_len = data.szTip.len() - 1;
	for (slot, unit) in data.szTip.iter_mut().zip(tip.encode_utf16().take(max_len)) {
		*slot = unit;
	}

	if !unsafe { Shell_NotifyIconW(NIM_ADD, &data) }.as_bool() {
		log::error!("Failed to add tray icon");
	}
}

fn remove_icon(hwnd: HWND) {
	let data = icon_data(hwnd);
	unsafe {
		let _ = Shell_NotifyIconW(NIM_DELETE, &data);
	}
}
