//! The monitoring loop shared by the app and the CLI: follow the default
//! device's provider and Discord, and apply the `ViewerEcho` rule.
//!
//! Core Audio notifications wake the loop as soon as something changes; the
//! slow poll is a safety net for events that never arrive.

use std::collections::{BTreeMap, HashSet};
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};
use std::thread;
use std::time::Duration;

use windows::core::Result;

use crate::audio::DeviceEnumerator;
use crate::discord;
use crate::events::Events;
use crate::provider::Provider;
use crate::viewer_echo::{self, ViewerEcho};

/// Wait between ticks when notifications are working.
pub const FALLBACK_POLL_INTERVAL: Duration = Duration::from_secs(30);
/// Wait between ticks when notifications could not be registered.
pub const POLL_INTERVAL: Duration = Duration::from_secs(2);
/// Time to let audio devices settle after the system resumes.
pub const RESUME_SETTLE_DELAY: Duration = Duration::from_secs(3);

const STOP_POLL: Duration = Duration::from_millis(100);

/// Sleep for `total`, waking early when any of `flags` becomes true.
pub fn sleep_unless(flags: &[&AtomicBool], total: Duration) {
	let mut waited = Duration::ZERO;
	while waited < total && !flags.iter().any(|flag| flag.load(Ordering::Relaxed)) {
		thread::sleep(STOP_POLL);
		waited += STOP_POLL;
	}
}

pub struct Monitor {
	enumerator: DeviceEnumerator,
	provider: Option<Provider>,
	rule: Option<ViewerEcho>,
	last_instances: BTreeMap<u32, &'static str>,
	wake: Arc<AtomicBool>,
	events: Option<Events>,
}

impl Monitor {
	pub fn new() -> Result<Self> {
		let wake = Arc::new(AtomicBool::new(false));
		Ok(Self {
			enumerator: DeviceEnumerator::new()?,
			provider: None,
			rule: None,
			last_instances: BTreeMap::new(),
			events: register_events(&wake),
			wake,
		})
	}

	/// Set when an audio event asks for an early tick. Pass it to [`sleep_unless`].
	pub fn wake_flag(&self) -> &AtomicBool {
		&self.wake
	}

	/// Run one pass. Returns how long to wait before the next one.
	pub fn tick(&mut self) -> Duration {
		// Cleared first, so an event during this pass triggers another one.
		self.wake.store(false, Ordering::Relaxed);
		if let Some(events) = self.events.as_mut() {
			events.sync();
		}

		let detected = Provider::detect_default(&self.enumerator);
		let instances = discord::running_instances();
		self.log_discord_changes(&instances);

		if detected != self.provider {
			log::info!(
				"Default device changed from {:?} to {detected:?}",
				self.provider
			);
			// Nothing is unmuted here: the new rule's pass unmutes Discord on the
			// recorded devices it does not cover, without a gap on the others.
			self.rule = None;
			self.provider = detected;
		}

		if self.create_rule()
			&& let Some(rule) = self.rule.as_mut()
		{
			if instances.is_empty() {
				rule.forget_exited(&instances);
			} else {
				rule.apply(&instances);
			}
		} else {
			viewer_echo::release_leftovers(&self.enumerator, &HashSet::new(), &instances);
		}

		if self.events.is_some() {
			FALLBACK_POLL_INTERVAL
		} else {
			POLL_INTERVAL
		}
	}

	/// Start over with fresh notifications, e.g. after the system resumes from
	/// sleep, in case the audio service restarted. Mutes stay in place, so
	/// Discord is not heard on the speakers in between.
	pub fn reset(&mut self) {
		self.rule = None;
		self.provider = None;
		self.events = None;
		self.events = register_events(&self.wake);
	}

	/// Unmute Discord on every device where Mutelith muted it.
	pub fn restore(&mut self) {
		self.rule = None;
		let instances = discord::running_instances();
		if viewer_echo::release_leftovers(&self.enumerator, &HashSet::new(), &instances) == 0 {
			log::info!("No Discord sessions to unmute");
		}
	}

	/// Create the rule for the current provider. Returns whether a rule exists.
	fn create_rule(&mut self) -> bool {
		if self.rule.is_none()
			&& let Some(provider) = self.provider
		{
			let events = self.events.as_ref().and_then(|e| e.session_events().ok());
			match ViewerEcho::new(provider, events) {
				Ok(rule) => {
					log::info!("Using {provider:?} for audio management");
					self.rule = Some(rule);
				}
				Err(err) => log::error!("Failed to create audio rule for {provider:?}: {err}"),
			}
		}
		self.rule.is_some()
	}

	fn log_discord_changes(&mut self, instances: &BTreeMap<u32, &'static str>) {
		for (pid, name) in &self.last_instances {
			if !instances.contains_key(pid) {
				log::info!("{name} (PID: {pid}) stopped");
			}
		}
		for (pid, name) in instances {
			if !self.last_instances.contains_key(pid) {
				log::info!("{name} (PID: {pid}) started");
			}
		}
		self.last_instances = instances.clone();
	}
}

fn register_events(wake: &Arc<AtomicBool>) -> Option<Events> {
	Events::new(Arc::clone(wake))
		.inspect_err(|err| {
			log::warn!("Audio notifications unavailable, polling every 2 seconds: {err}")
		})
		.ok()
}
