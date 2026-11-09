//! Thin wrappers over the Windows Core Audio COM API.

use std::fmt::Display;

use windows::Win32::Devices::FunctionDiscovery::{
	PKEY_Device_FriendlyName, PKEY_DeviceInterface_FriendlyName,
};
use windows::Win32::Media::Audio::{
	DEVICE_STATE_ACTIVE, IAudioSessionControl, IAudioSessionControl2, IAudioSessionEvents,
	IAudioSessionManager2, IMMDevice, IMMDeviceEnumerator, ISimpleAudioVolume, MMDeviceEnumerator,
	eMultimedia, eRender,
};
use windows::Win32::System::Com::{
	CLSCTX_ALL, COINIT_MULTITHREADED, CoCreateInstance, CoInitializeEx, CoTaskMemFree,
	CoUninitialize, STGM_READ,
};
use windows::Win32::UI::Shell::PropertiesSystem::IPropertyStore;
use windows::core::{Interface, Result};

/// Keeps COM initialized (multithreaded apartment) on the current thread.
pub struct ComGuard(());

impl ComGuard {
	pub fn init() -> Result<Self> {
		unsafe { CoInitializeEx(None, COINIT_MULTITHREADED).ok()? };
		Ok(Self(()))
	}
}

impl Drop for ComGuard {
	fn drop(&mut self) {
		unsafe { CoUninitialize() };
	}
}

pub struct DeviceEnumerator(pub(crate) IMMDeviceEnumerator);

impl DeviceEnumerator {
	pub fn new() -> Result<Self> {
		unsafe { CoCreateInstance(&MMDeviceEnumerator, None, CLSCTX_ALL) }.map(Self)
	}

	/// Active output (render) devices. Devices that fail to load are skipped.
	pub fn active_render_devices(&self) -> Result<Vec<Device>> {
		unsafe {
			let collection = self.0.EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE)?;
			let count = collection.GetCount()?;
			Ok((0..count)
				.filter_map(|i| collection.Item(i).ok())
				.map(Device)
				.collect())
		}
	}

	pub fn default_render_device(&self) -> Result<Device> {
		unsafe { self.0.GetDefaultAudioEndpoint(eRender, eMultimedia) }.map(Device)
	}
}

pub struct Device(IMMDevice);

impl Device {
	pub fn friendly_name(&self) -> String {
		self.property(|store| unsafe { store.GetValue(&PKEY_Device_FriendlyName) })
	}

	/// Name of the audio adapter behind this endpoint, e.g.
	/// `SteelSeries Sonar Virtual Audio Device`.
	pub fn controller_info(&self) -> String {
		self.property(|store| unsafe { store.GetValue(&PKEY_DeviceInterface_FriendlyName) })
	}

	/// Endpoint ID string, stable while the device exists. Treat it as opaque.
	pub fn id(&self) -> Result<String> {
		unsafe {
			let id = self.0.GetId()?;
			let text = id.to_string();
			CoTaskMemFree(Some(id.0 as _));
			Ok(text?)
		}
	}

	pub(crate) fn session_manager(&self) -> Result<IAudioSessionManager2> {
		unsafe { self.0.Activate(CLSCTX_ALL, None) }
	}

	/// Audio sessions on this device. Sessions that fail to load are skipped.
	pub fn sessions(&self) -> Result<Vec<Session>> {
		unsafe {
			let sessions = self.session_manager()?.GetSessionEnumerator()?;
			let count = sessions.GetCount()?;
			Ok((0..count)
				.filter_map(|i| sessions.GetSession(i).ok())
				.map(Session)
				.collect())
		}
	}

	fn property<V: Display>(&self, read: impl FnOnce(&IPropertyStore) -> Result<V>) -> String {
		unsafe { self.0.OpenPropertyStore(STGM_READ) }
			.and_then(|store| read(&store))
			.map(|value| value.to_string())
			.unwrap_or_default()
	}
}

pub struct Session(IAudioSessionControl);

impl Session {
	/// Owning process ID, or 0 for system sounds and unknown owners.
	pub fn process_id(&self) -> u32 {
		self
			.0
			.cast::<IAudioSessionControl2>()
			.and_then(|control| unsafe { control.GetProcessId() })
			.unwrap_or(0)
	}

	pub fn is_muted(&self) -> bool {
		self
			.volume()
			.and_then(|v| unsafe { v.GetMute() })
			.map(|m| m.as_bool())
			.unwrap_or(false)
	}

	pub fn set_mute(&self, mute: bool) -> Result<()> {
		unsafe { self.volume()?.SetMute(mute, std::ptr::null()) }
	}

	pub fn volume_level(&self) -> Result<f32> {
		unsafe { self.volume()?.GetMasterVolume() }
	}

	pub fn set_volume(&self, level: f32) -> Result<()> {
		unsafe { self.volume()?.SetMasterVolume(level, std::ptr::null()) }
	}

	/// Call `events` when this session's volume or mute changes. Keep the
	/// returned control to unregister with [`unwatch`].
	pub(crate) fn watch(&self, events: &IAudioSessionEvents) -> Result<IAudioSessionControl> {
		unsafe { self.0.RegisterAudioSessionNotification(events)? };
		Ok(self.0.clone())
	}

	fn volume(&self) -> Result<ISimpleAudioVolume> {
		self.0.cast()
	}
}

pub(crate) fn unwatch(control: &IAudioSessionControl, events: &IAudioSessionEvents) {
	unsafe {
		let _ = control.UnregisterAudioSessionNotification(events);
	}
}
