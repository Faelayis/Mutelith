//! Output devices where Mutelith muted Discord, kept in the registry.
//!
//! Windows saves Discord's per-app mute for each output device, so a mute
//! outlives Mutelith when it is not undone (Discord closed at exit, a crash, a
//! killed process). The record lets a later run find and undo those leftovers.

use std::io;

use winreg::RegKey;
use winreg::enums::{HKEY_CURRENT_USER, KEY_READ, KEY_SET_VALUE};

const APP_KEY: &str = r"Software\Mutelith";
const ENDPOINTS_KEY: &str = r"Software\Mutelith\MutedEndpoints";

/// Registry value name for one Discord build on one endpoint. Windows saves
/// the mute per app, so each build needs its own record.
fn value_name(app: &str, endpoint_id: &str) -> String {
	format!("{app}|{endpoint_id}")
}

/// Whether Mutelith muted this Discord build on this endpoint and has not
/// undone it yet.
pub fn is_recorded(app: &str, endpoint_id: &str) -> bool {
	RegKey::predef(HKEY_CURRENT_USER)
		.open_subkey_with_flags(ENDPOINTS_KEY, KEY_READ)
		.and_then(|key| key.get_value::<u32, _>(value_name(app, endpoint_id)))
		.is_ok_and(|value| value != 0)
}

/// Remember that Mutelith mutes this Discord build on this endpoint. Call it
/// before muting.
pub fn record(app: &str, endpoint_id: &str) {
	if is_recorded(app, endpoint_id) {
		return;
	}
	let result = RegKey::predef(HKEY_CURRENT_USER)
		.create_subkey(ENDPOINTS_KEY)
		.and_then(|(key, _)| key.set_value(value_name(app, endpoint_id), &1u32));
	if let Err(err) = result {
		log::warn!("Failed to record muted device {endpoint_id} for {app}: {err}");
	}
}

/// Forget an endpoint once this Discord build is unmuted there.
pub fn forget(app: &str, endpoint_id: &str) {
	let result = RegKey::predef(HKEY_CURRENT_USER)
		.open_subkey_with_flags(ENDPOINTS_KEY, KEY_SET_VALUE)
		.and_then(|key| key.delete_value(value_name(app, endpoint_id)));
	match result {
		Ok(()) => {}
		Err(err) if err.kind() == io::ErrorKind::NotFound => {}
		Err(err) => log::warn!("Failed to forget muted device {endpoint_id} for {app}: {err}"),
	}
}

/// Delete everything Mutelith keeps in the registry under `Software\Mutelith`.
pub fn clear() {
	match RegKey::predef(HKEY_CURRENT_USER).delete_subkey_all(APP_KEY) {
		Ok(()) => log::info!("Removed muted device records"),
		Err(err) if err.kind() == io::ErrorKind::NotFound => {}
		Err(err) => log::error!("Failed to remove muted device records: {err}"),
	}
}
