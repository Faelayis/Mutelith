//! Core Audio notifications that wake the monitoring loop early: the default
//! output device changes, a device appears or goes away, or an app opens a new
//! audio session (e.g. Discord starts a screen share), or the volume or mute
//! of a session Mutelith muted changes (Discord or the user unmuted it).
//!
//! Callbacks only set a flag. Microsoft requires `IMMNotificationClient`
//! methods to be nonblocking, and the loop does the real work on its own thread.

use std::collections::HashMap;
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};

use windows::Win32::Foundation::PROPERTYKEY;
use windows::Win32::Media::Audio::{
	AudioSessionDisconnectReason, AudioSessionState, DEVICE_STATE, EDataFlow, ERole,
	IAudioSessionControl, IAudioSessionEvents, IAudioSessionEvents_Impl, IAudioSessionManager2,
	IAudioSessionNotification, IAudioSessionNotification_Impl, IMMNotificationClient,
	IMMNotificationClient_Impl, eRender,
};
use windows::core::{BOOL, GUID, Interface, PCWSTR, Ref, Result, implement};

use crate::audio::DeviceEnumerator;

#[implement(IMMNotificationClient, IAudioSessionNotification, IAudioSessionEvents)]
struct Waker(Arc<AtomicBool>);

impl Waker {
	fn wake(&self) {
		self.0.store(true, Ordering::Relaxed);
	}
}

impl IMMNotificationClient_Impl for Waker_Impl {
	fn OnDeviceStateChanged(&self, _: &PCWSTR, _: DEVICE_STATE) -> Result<()> {
		self.wake();
		Ok(())
	}

	fn OnDeviceAdded(&self, _: &PCWSTR) -> Result<()> {
		self.wake();
		Ok(())
	}

	fn OnDeviceRemoved(&self, _: &PCWSTR) -> Result<()> {
		self.wake();
		Ok(())
	}

	fn OnDefaultDeviceChanged(&self, flow: EDataFlow, _: ERole, _: &PCWSTR) -> Result<()> {
		if flow == eRender {
			self.wake();
		}
		Ok(())
	}

	// Fires often (volume, formats) and never changes what the rule does.
	fn OnPropertyValueChanged(&self, _: &PCWSTR, _: &PROPERTYKEY) -> Result<()> {
		Ok(())
	}
}

impl IAudioSessionNotification_Impl for Waker_Impl {
	fn OnSessionCreated(&self, _: Ref<IAudioSessionControl>) -> Result<()> {
		self.wake();
		Ok(())
	}
}

impl IAudioSessionEvents_Impl for Waker_Impl {
	// Also fires for Mutelith's own changes; the extra pass then finds the
	// session muted and changes nothing, so it cannot loop.
	fn OnSimpleVolumeChanged(&self, _: f32, _: BOOL, _: *const GUID) -> Result<()> {
		self.wake();
		Ok(())
	}

	fn OnDisplayNameChanged(&self, _: &PCWSTR, _: *const GUID) -> Result<()> {
		Ok(())
	}

	fn OnIconPathChanged(&self, _: &PCWSTR, _: *const GUID) -> Result<()> {
		Ok(())
	}

	fn OnChannelVolumeChanged(&self, _: u32, _: *const f32, _: u32, _: *const GUID) -> Result<()> {
		Ok(())
	}

	fn OnGroupingParamChanged(&self, _: *const GUID, _: *const GUID) -> Result<()> {
		Ok(())
	}

	fn OnStateChanged(&self, _: AudioSessionState) -> Result<()> {
		Ok(())
	}

	fn OnSessionDisconnected(&self, _: AudioSessionDisconnectReason) -> Result<()> {
		Ok(())
	}
}

/// Registered notifications. Dropping it unregisters them; drop it on the
/// thread that holds COM (MTA) before COM is uninitialized.
pub struct Events {
	enumerator: DeviceEnumerator,
	devices: IMMNotificationClient,
	sessions: IAudioSessionNotification,
	/// Session managers registered for new-session notifications, by device ID.
	managers: HashMap<String, IAudioSessionManager2>,
}

impl Events {
	/// Callback to register on each session the rule mutes.
	pub fn session_events(&self) -> Result<IAudioSessionEvents> {
		self.devices.cast()
	}

	/// Register for device notifications. `wake` is set on every relevant event.
	/// Needs COM initialized as MTA on this thread, or session notifications never arrive.
	pub fn new(wake: Arc<AtomicBool>) -> Result<Self> {
		let enumerator = DeviceEnumerator::new()?;
		let devices: IMMNotificationClient = Waker(wake).into();
		let sessions = devices.cast()?;
		unsafe {
			enumerator
				.0
				.RegisterEndpointNotificationCallback(&devices)?
		};
		Ok(Self {
			enumerator,
			devices,
			sessions,
			managers: HashMap::new(),
		})
	}

	/// Register new-session notifications on every active output device and drop
	/// devices that are gone. Call it from the loop, never from a callback.
	pub fn sync(&mut self) {
		let devices = match self.enumerator.active_render_devices() {
			Ok(devices) => devices,
			Err(err) => {
				log::warn!("Failed to list audio devices for notifications: {err}");
				return;
			}
		};

		let mut current = HashMap::new();
		for device in devices {
			let Ok(id) = device.id() else { continue };
			if let Some(manager) = self.managers.remove(&id) {
				current.insert(id, manager);
				continue;
			}

			let registered = device.session_manager().and_then(|manager| unsafe {
				manager.RegisterSessionNotification(&self.sessions)?;
				// Windows discards new-session notifications until the existing
				// sessions have been counted once.
				manager.GetSessionEnumerator()?.GetCount()?;
				Ok(manager)
			});
			match registered {
				Ok(manager) => {
					current.insert(id, manager);
				}
				Err(err) => log::warn!(
					"Failed to watch sessions on {}: {err}",
					device.friendly_name()
				),
			}
		}

		// What is left belongs to devices that are no longer active.
		self.unregister_sessions();
		self.managers = current;
	}

	fn unregister_sessions(&mut self) {
		for manager in self.managers.values() {
			unsafe {
				let _ = manager.UnregisterSessionNotification(&self.sessions);
			}
		}
		self.managers.clear();
	}
}

impl Drop for Events {
	fn drop(&mut self) {
		self.unregister_sessions();
		unsafe {
			let _ = self
				.enumerator
				.0
				.UnregisterEndpointNotificationCallback(&self.devices);
		}
	}
}
