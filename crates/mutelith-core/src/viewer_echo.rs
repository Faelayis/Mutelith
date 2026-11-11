//! Rule `ViewerEcho`: mute Discord on every output device that causes echo for
//! viewers, keep it on the provider's target device, and undo it on restore.
//!
//! Only the mute is changed, never the volume. Windows saves Discord's mute per
//! output device, so every device Mutelith mutes on is recorded in [`store`]
//! first, and passes that find a recorded device outside the rule unmute
//! Discord there. That also undoes leftovers from an earlier unclean exit.

use std::collections::{BTreeMap, HashMap, HashSet};

use windows::Win32::Media::Audio::{IAudioSessionControl, IAudioSessionEvents};
use windows::core::Result;

use crate::audio::{self, Device, DeviceEnumerator};
use crate::contains_ci;
use crate::discord;
use crate::provider::Provider;
use crate::store;

/// A muted Discord session: endpoint ID and session instance ID.
type SessionKey = (String, String);

struct Muted {
	pid: u32,
	/// Registered for volume events, unregistered when the entry goes away.
	watched: Option<IAudioSessionControl>,
}

pub struct ViewerEcho {
	provider: Provider,
	enumerator: DeviceEnumerator,
	muted: HashMap<SessionKey, Muted>,
	/// Sessions already muted by the user; left alone.
	user_muted: HashSet<SessionKey>,
	last_count: Option<usize>,
	/// The target device was missing on the last pass, and that was logged.
	target_missing: bool,
	/// Called when a muted session's volume or mute changes, so it is muted
	/// again right away instead of on the next poll.
	events: Option<IAudioSessionEvents>,
}

impl ViewerEcho {
	pub fn new(provider: Provider, events: Option<IAudioSessionEvents>) -> Result<Self> {
		Ok(Self {
			provider,
			enumerator: DeviceEnumerator::new()?,
			muted: HashMap::new(),
			user_muted: HashSet::new(),
			last_count: None,
			target_missing: false,
			events,
		})
	}

	pub fn provider(&self) -> Provider {
		self.provider
	}

	/// Mute Discord where it causes echo, and unmute it on recorded devices the
	/// rule no longer covers. `instances` are the running Discord processes.
	/// Returns the number of muted sessions.
	pub fn apply(&mut self, instances: &BTreeMap<u32, &str>) -> usize {
		self.forget_exited(instances);

		let (target, prefix) = (self.provider.target_device(), self.provider.device_prefix());
		let Some(devices) = list_devices(&self.enumerator) else {
			return 0;
		};

		if !devices.names.iter().any(|name| contains_ci(name, target)) {
			if !self.target_missing {
				log::warn!("{target} not found");
				self.target_missing = true;
				// The session count is logged again once the device is back.
				self.last_count = None;
			}
			return 0;
		}
		self.target_missing = false;

		let targets = mute_targets(&devices.names, target, prefix);
		let count = targets
			.iter()
			.map(|&i| self.mute_in(&devices, i, instances))
			.sum();

		let keep: HashSet<&str> = targets.iter().map(|&i| devices.ids[i].as_str()).collect();
		release_in(&devices, &keep, instances);

		if self.last_count != Some(count) {
			if count > 0 {
				log::info!("Total Discord sessions muted: {count}");
			} else {
				log::info!("Waiting for Discord audio session (e.g., screen share, streaming)");
			}
			self.last_count = Some(count);
		}

		count
	}

	/// Drop entries for Discord processes that have exited.
	pub fn forget_exited(&mut self, instances: &BTreeMap<u32, &str>) {
		let events = self.events.as_ref();
		self.muted.retain(|_, muted| {
			let alive = instances.contains_key(&muted.pid);
			if !alive {
				unwatch(events, muted);
			}
			alive
		});
	}

	fn mute_in(&mut self, devices: &Devices, i: usize, instances: &BTreeMap<u32, &str>) -> usize {
		let (device, id, device_name) = (&devices.list[i], &devices.ids[i], &devices.names[i]);
		let mut count = 0;

		for session in device.sessions().unwrap_or_default() {
			let pid = session.process_id();
			let Some(name) = instances.get(&pid) else {
				continue;
			};
			// An empty ID still mutes; it only shares one entry with other failures.
			let instance = session.instance_id().unwrap_or_default();

			let key = (id.clone(), instance);
			let known = self.muted.contains_key(&key);
			let muted = session.muted().ok();
			if known && muted == Some(true) {
				count += 1;
				continue;
			}
			if !known && let Some(muted) = muted {
				let seen = self.user_muted.contains(&key);
				if is_user_mute(muted, seen, || store::is_recorded(name, id)) {
					self.user_muted.insert(key);
					continue;
				}
				if !muted {
					self.user_muted.remove(&key);
				}
			}

			// Recorded first, so a crash right after muting still leaves a record.
			store::record(name, id);
			if session.set_mute(true).is_err() {
				continue;
			}

			count += 1;
			if known {
				continue;
			}

			log::info!("{name} (PID: {pid}) muted in {device_name}");
			let watched = self
				.events
				.as_ref()
				.and_then(|events| match session.watch(events) {
					Ok(control) => Some(control),
					Err(err) => {
						log::warn!("Failed to watch {name} (PID: {pid}) volume: {err}");
						None
					}
				});
			self.muted.insert(key, Muted { pid, watched });
		}

		count
	}
}

impl Drop for ViewerEcho {
	fn drop(&mut self) {
		let events = self.events.as_ref();
		for muted in self.muted.values() {
			unwatch(events, muted);
		}
	}
}

/// Unmute Discord on every recorded device whose endpoint ID is not in `keep`.
/// Returns the number of unmuted sessions.
pub fn release_leftovers(
	enumerator: &DeviceEnumerator,
	keep: &HashSet<&str>,
	instances: &BTreeMap<u32, &str>,
) -> usize {
	list_devices(enumerator).map_or(0, |devices| release_in(&devices, keep, instances))
}

struct Devices {
	list: Vec<Device>,
	ids: Vec<String>,
	names: Vec<String>,
}

fn list_devices(enumerator: &DeviceEnumerator) -> Option<Devices> {
	match enumerator.active_render_devices() {
		Ok(list) => {
			let ids = list.iter().map(|d| d.id().unwrap_or_default()).collect();
			let names = list.iter().map(Device::friendly_name).collect();
			Some(Devices { list, ids, names })
		}
		Err(err) => {
			log::error!("Failed to list audio devices: {err}");
			None
		}
	}
}

fn release_in(devices: &Devices, keep: &HashSet<&str>, instances: &BTreeMap<u32, &str>) -> usize {
	// Discord's saved mute can only be reached through a session of a running Discord.
	if instances.is_empty() {
		return 0;
	}

	let mut count = 0;
	let leftovers = leftover_targets(
		&devices.ids,
		keep,
		&discord::PROCESS_NAMES,
		store::is_recorded,
	);
	for (i, app) in leftovers {
		let (id, device_name) = (&devices.ids[i], &devices.names[i]);
		let mut found = false;
		let mut failed = false;

		for session in devices.list[i].sessions().unwrap_or_default() {
			let pid = session.process_id();
			if instances.get(&pid) != Some(&app) {
				continue;
			}
			found = true;
			if !session.is_muted() {
				continue;
			}
			match session.set_mute(false) {
				Ok(()) => {
					count += 1;
					log::info!("{app} (PID: {pid}) unmuted in {device_name}");
				}
				Err(err) => {
					failed = true;
					log::warn!("Failed to unmute {app} (PID: {pid}) in {device_name}: {err}");
				}
			}
		}

		// Kept until a session of that Discord build shows up there, so its
		// saved mute is undone.
		if found && !failed {
			store::forget(app, id);
		}
	}

	if count > 0 {
		log::info!("Unmuted {count} Discord session(s)");
	}
	count
}

fn unwatch(events: Option<&IAudioSessionEvents>, muted: &Muted) {
	if let (Some(events), Some(control)) = (events, &muted.watched) {
		audio::unwatch(control, events);
	}
}

/// Whether a muted session Mutelith did not mute belongs to the user and is
/// left alone. `seen` is an earlier pass's answer for this session. A record
/// for the device means the mute is Mutelith's own leftover.
fn is_user_mute(muted: bool, seen: bool, recorded: impl FnOnce() -> bool) -> bool {
	muted && (seen || !recorded())
}

/// Devices to mute Discord on: the first target device, then every device
/// outside the provider's prefix.
fn mute_targets(names: &[String], target: &str, prefix: &str) -> Vec<usize> {
	let first_target = names.iter().position(|name| contains_ci(name, target));
	let outside = names
		.iter()
		.enumerate()
		.filter(|(_, name)| !contains_ci(name, prefix))
		.map(|(i, _)| i);
	first_target.into_iter().chain(outside).collect()
}

/// Device and Discord build pairs to unmute: recorded ones whose device is not
/// in `keep`. Windows saves the mute per app, so each build is its own record.
fn leftover_targets<'a>(
	ids: &[String],
	keep: &HashSet<&str>,
	apps: &[&'a str],
	is_recorded: impl Fn(&str, &str) -> bool,
) -> Vec<(usize, &'a str)> {
	let mut targets = Vec::new();
	for (i, id) in ids.iter().enumerate() {
		if id.is_empty() || keep.contains(id.as_str()) {
			continue;
		}
		for &app in apps {
			if is_recorded(app, id) {
				targets.push((i, app));
			}
		}
	}
	targets
}

#[cfg(test)]
mod tests {
	use super::*;

	fn sonar_devices() -> Vec<String> {
		[
			"Speakers (Realtek(R) Audio)",
			"SteelSeries Sonar - Gaming",
			"SteelSeries Sonar - Chat",
			"SteelSeries Sonar - Microphone",
			"Headphones (Arctis Nova)",
		]
		.map(String::from)
		.to_vec()
	}

	#[test]
	fn mute_skips_other_provider_devices() {
		let p = Provider::SteelSeriesSonar;
		assert_eq!(
			mute_targets(&sonar_devices(), p.target_device(), p.device_prefix()),
			vec![3, 0, 4]
		);
	}

	#[test]
	fn mute_without_target_only_outside_devices() {
		let names = ["Speakers".to_string(), "FxSound Virtual".to_string()];
		let p = Provider::FxSound;
		assert_eq!(
			mute_targets(&names, p.target_device(), p.device_prefix()),
			vec![0]
		);
	}

	#[test]
	fn user_mute_is_a_mute_without_a_record() {
		assert!(is_user_mute(true, false, || false));
		assert!(!is_user_mute(true, false, || true));
		assert!(!is_user_mute(false, false, || false));
	}

	#[test]
	fn user_mute_stays_after_a_record_appears() {
		assert!(is_user_mute(true, true, || true));
		assert!(!is_user_mute(false, true, || true));
	}

	#[test]
	fn leftovers_are_recorded_devices_outside_keep() {
		let ids = ["a", "b", "c", ""].map(String::from).to_vec();
		let keep = HashSet::from(["a"]);
		let recorded = |_: &str, id: &str| id == "a" || id == "c";
		assert_eq!(
			leftover_targets(&ids, &keep, &["Discord"], recorded),
			vec![(2, "Discord")]
		);
	}

	#[test]
	fn leftovers_are_per_discord_build() {
		let ids = ["a".to_string()];
		let recorded = |app: &str, _: &str| app != "DiscordPTB";
		assert_eq!(
			leftover_targets(
				&ids,
				&HashSet::new(),
				&["Discord", "DiscordPTB", "DiscordCanary"],
				recorded
			),
			vec![(0, "Discord"), (0, "DiscordCanary")]
		);
	}

	#[test]
	fn no_leftovers_when_everything_is_kept() {
		let ids = ["a", "b"].map(String::from).to_vec();
		let keep = HashSet::from(["a", "b"]);
		assert!(leftover_targets(&ids, &keep, &["Discord"], |_, _| true).is_empty());
	}
}
