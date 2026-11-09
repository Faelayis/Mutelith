//! Dark mode for the tray menu. Win32 popup menus are always light unless the
//! app opts in through `uxtheme.dll` functions that Windows exports only by
//! ordinal (Windows 10 1809 and later). Notepad++ and Windows Terminal use the
//! same calls. On systems without them, every call here does nothing.

use std::sync::OnceLock;

use windows::Win32::Foundation::HWND;
use windows::Win32::System::LibraryLoader::{GetProcAddress, LoadLibraryW};
use windows::core::{PCSTR, w};

/// `SetPreferredAppMode` (1903+) or `AllowDarkModeForApp` (1809). Both treat 1
/// as "follow the system app theme".
const ORDINAL_SET_PREFERRED_APP_MODE: u16 = 135;
const ORDINAL_ALLOW_DARK_MODE_FOR_WINDOW: u16 = 133;
const ORDINAL_REFRESH_IMMERSIVE_COLOR_POLICY_STATE: u16 = 104;
const ORDINAL_FLUSH_MENU_THEMES: u16 = 136;
const APP_MODE_ALLOW_DARK: i32 = 1;

type SetPreferredAppMode = unsafe extern "system" fn(i32) -> i32;
type AllowDarkModeForWindow = unsafe extern "system" fn(HWND, i32) -> i32;
type NoArgs = unsafe extern "system" fn();

struct UxTheme {
	set_preferred_app_mode: Option<SetPreferredAppMode>,
	allow_dark_mode_for_window: Option<AllowDarkModeForWindow>,
	refresh_color_policy: Option<NoArgs>,
	flush_menu_themes: Option<NoArgs>,
}

fn uxtheme() -> &'static UxTheme {
	static UXTHEME: OnceLock<UxTheme> = OnceLock::new();
	UXTHEME.get_or_init(|| unsafe {
		let module = LoadLibraryW(w!("uxtheme.dll")).ok();
		let by_ordinal = |ordinal: u16| {
			module.and_then(|m| GetProcAddress(m, PCSTR(ordinal as usize as *const u8)))
		};
		UxTheme {
			set_preferred_app_mode: by_ordinal(ORDINAL_SET_PREFERRED_APP_MODE)
				.map(|f| std::mem::transmute(f)),
			allow_dark_mode_for_window: by_ordinal(ORDINAL_ALLOW_DARK_MODE_FOR_WINDOW)
				.map(|f| std::mem::transmute(f)),
			refresh_color_policy: by_ordinal(ORDINAL_REFRESH_IMMERSIVE_COLOR_POLICY_STATE)
				.map(|f| std::mem::transmute(f)),
			flush_menu_themes: by_ordinal(ORDINAL_FLUSH_MENU_THEMES).map(|f| std::mem::transmute(f)),
		}
	})
}

/// Let menus follow the system light/dark app theme. Call before creating windows.
pub fn follow_system() {
	let ux = uxtheme();
	unsafe {
		if let Some(set_mode) = ux.set_preferred_app_mode {
			set_mode(APP_MODE_ALLOW_DARK);
		}
		refresh();
	}
}

/// Opt a window in, so menus it owns can draw dark.
pub fn allow_for_window(hwnd: HWND) {
	if let Some(allow) = uxtheme().allow_dark_mode_for_window {
		unsafe {
			allow(hwnd, 1);
		}
	}
}

/// Re-read the system theme, e.g. after the user switches light/dark mode.
pub fn refresh() {
	let ux = uxtheme();
	unsafe {
		if let Some(refresh_policy) = ux.refresh_color_policy {
			refresh_policy();
		}
		if let Some(flush) = ux.flush_menu_themes {
			flush();
		}
	}
}
