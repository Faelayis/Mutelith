//! Rule `ViewerEcho`: mute Discord on every output device that causes echo for
//! viewers, keep it on the provider's target device, and undo it on restore.

use std::collections::{BTreeMap, HashMap};

use windows::Win32::Media::Audio::{IAudioSessionControl, IAudioSessionEvents};
use windows::core::Result;

use crate::audio::{self, Device, DeviceEnumerator};
use crate::contains_ci;
use crate::discord;
use crate::provider::Provider;

/// A muted Discord session: device name and process ID.
type SessionKey = (String, u32);

pub struct ViewerEcho {
	provider: Provider,
	enumerator: DeviceEnumerator,
	/// Muted sessions and the volume each had before Mutelith muted it.
	// shortcut: one volume per process and device, so several sessions of one
	// process on one device all get the first one's volume back; key by
	// session instance ID if that ever matters.
	muted: HashMap<SessionKey, f32>,
	last_count: Option<usize>,
	/// Called when a muted session's volume or mute changes, so it is muted
	/// again right away instead of on the next poll.
	events: Option<IAudioSessionEvents>,
	watched: Vec<IAudioSessionControl>,
}

impl ViewerEcho {
	pub fn new(provider: Provider, events: Option<IAudioSessionEvents>) -> Result<Self> {
		Ok(Self {
			provider,
			enumerator: DeviceEnumerator::new()?,
			muted: HashMap::new(),
			last_count: None,
			events,
			watched: Vec::new(),
		})
	}

	pub fn provider(&self) -> Provider {
		self.provider
	}

	/// Forget earlier mutes, then apply.
	pub fn initialize(&mut self) -> usize {
		self.unwatch_all();
		self.muted.clear();
		self.apply()
	}

	/// Mute Discord where it causes echo. Returns the number of muted sessions.
	pub fn apply(&mut self) -> usize {
		let (target, prefix) = (self.provider.target_device(), self.provider.device_prefix());
		let Some((devices, names)) = self.devices() else {
			return 0;
		};

		if !names.iter().any(|name| contains_ci(name, target)) {
			log::warn!("{target} not found");
			return 0;
		}

		let instances = discord::running_instances();
		let count = mute_targets(&names, target, prefix)
			.into_iter()
			.map(|i| self.mute_in(&devices[i], &names[i], &instances))
			.sum();

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

	/// Unmute every session this rule muted. Returns the number of unmuted sessions.
	pub fn restore(&mut self) -> usize {
		if self.muted.is_empty() {
			return 0;
		}

		let (target, prefix) = (self.provider.target_device(), self.provider.device_prefix());
		let count = match self.devices() {
			Some((devices, names)) => {
				let instances = discord::running_instances();
				restore_targets(&names, target, prefix)
					.into_iter()
					.map(|i| self.unmute_in(&devices[i], &names[i], &instances))
					.sum()
			}
			None => 0,
		};

		if count > 0 {
			log::info!("Unmuted {count} Discord session(s)");
		} else {
			log::info!("No Discord sessions to unmute");
		}

		self.unwatch_all();
		self.muted.clear();
		self.last_count = None;
		count
	}

	fn unwatch_all(&mut self) {
		if let Some(events) = &self.events {
			for control in self.watched.drain(..) {
				audio::unwatch(&control, events);
			}
		}
	}

	fn devices(&self) -> Option<(Vec<Device>, Vec<String>)> {
		match self.enumerator.active_render_devices() {
			Ok(devices) => {
				let names = devices.iter().map(Device::friendly_name).collect();
				Some((devices, names))
			}
			Err(err) => {
				log::error!("Failed to list audio devices: {err}");
				None
			}
		}
	}

	fn mute_in(
		&mut self,
		device: &Device,
		device_name: &str,
		instances: &BTreeMap<u32, &str>,
	) -> usize {
		let mut count = 0;

		for session in device.sessions().unwrap_or_default() {
			let pid = session.process_id();
			let Some(name) = instances.get(&pid) else {
				continue;
			};

			let key = (device_name.to_owned(), pid);
			let known = self.muted.contains_key(&key);
			if known && session.is_muted() {
				count += 1;
				continue;
			}

			// Read before muting; a session muted again keeps its first saved volume.
			let volume = session.volume_level().unwrap_or(1.0);
			if session
				.set_volume(0.0)
				.and_then(|()| session.set_mute(true))
				.is_err()
			{
				continue;
			}

			count += 1;
			if !known {
				self.muted.insert(key, volume);
				log::info!("{name} (PID: {pid}) muted in {device_name}");
				if let Some(events) = &self.events {
					match session.watch(events) {
						Ok(control) => self.watched.push(control),
						Err(err) => log::warn!("Failed to watch {name} (PID: {pid}) volume: {err}"),
					}
				}
			}
		}

		count
	}

	fn unmute_in(
		&self,
		device: &Device,
		device_name: &str,
		instances: &BTreeMap<u32, &str>,
	) -> usize {
		let mut count = 0;

		for session in device.sessions().unwrap_or_default() {
			let pid = session.process_id();
			let Some(&volume) = self.muted.get(&(device_name.to_owned(), pid)) else {
				continue;
			};
			let Some(name) = instances.get(&pid) else {
				continue;
			};

			if session
				.set_mute(false)
				.and_then(|()| session.set_volume(volume))
				.is_ok()
			{
				count += 1;
				log::info!("{name} (PID: {pid}) unmuted in {device_name}");
			}
		}

		count
	}
}

impl Drop for ViewerEcho {
	fn drop(&mut self) {
		self.unwatch_all();
	}
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

/// Devices to unmute Discord on: every target device and every device outside
/// the provider's prefix.
fn restore_targets(names: &[String], target: &str, prefix: &str) -> Vec<usize> {
	names
		.iter()
		.enumerate()
		.filter(|(_, name)| contains_ci(name, target) || !contains_ci(name, prefix))
		.map(|(i, _)| i)
		.collect()
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
	fn restore_covers_target_and_outside_devices() {
		let p = Provider::SteelSeriesSonar;
		assert_eq!(
			restore_targets(&sonar_devices(), p.target_device(), p.device_prefix()),
			vec![0, 3, 4]
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
}
