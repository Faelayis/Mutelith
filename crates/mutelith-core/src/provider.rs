//! Supported virtual audio stacks and how to recognize them.

use crate::audio::DeviceEnumerator;
use crate::contains_ci;

const SONAR_PREFIX: &str = "SteelSeries Sonar";
const SONAR_MICROPHONE: &str = "SteelSeries Sonar - Microphone";
const SONAR_CONTROLLER: &str = "SteelSeries Sonar Virtual Audio Device";

const FXSOUND_PREFIX: &str = "FxSound";
const FXSOUND_SPEAKERS: &str = "FxSound Speakers";
const FXSOUND_CONTROLLER: &str = "FxSound Audio Enhancer";

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Provider {
	SteelSeriesSonar,
	FxSound,
}

impl Provider {
	/// The provider device where Discord is muted too, alongside every device outside the provider.
	pub fn target_device(self) -> &'static str {
		match self {
			Self::SteelSeriesSonar => SONAR_MICROPHONE,
			Self::FxSound => FXSOUND_SPEAKERS,
		}
	}

	/// Name prefix shared by all devices this provider creates.
	pub fn device_prefix(self) -> &'static str {
		match self {
			Self::SteelSeriesSonar => SONAR_PREFIX,
			Self::FxSound => FXSOUND_PREFIX,
		}
	}

	/// Recognize a provider from a device's friendly name and controller info.
	pub fn detect(friendly_name: &str, controller_info: &str) -> Option<Self> {
		let either =
			|needle| contains_ci(friendly_name, needle) || contains_ci(controller_info, needle);

		if either(SONAR_CONTROLLER) {
			Some(Self::SteelSeriesSonar)
		} else if either(FXSOUND_CONTROLLER) || either(FXSOUND_PREFIX) {
			Some(Self::FxSound)
		} else {
			None
		}
	}

	/// Provider behind the default multimedia output device, if any.
	pub fn detect_default(enumerator: &DeviceEnumerator) -> Option<Self> {
		match enumerator.default_render_device() {
			Ok(device) => Self::detect(&device.friendly_name(), &device.controller_info()),
			Err(err) => {
				log::warn!("No default audio device found: {err}");
				None
			}
		}
	}
}

#[cfg(test)]
mod tests {
	use super::*;

	#[test]
	fn detects_sonar_by_controller() {
		assert_eq!(
			Provider::detect(
				"Headphones (Sonar)",
				"SteelSeries Sonar Virtual Audio Device"
			),
			Some(Provider::SteelSeriesSonar)
		);
	}

	#[test]
	fn sonar_name_alone_is_not_enough() {
		assert_eq!(Provider::detect("SteelSeries Sonar - Gaming", ""), None);
	}

	#[test]
	fn detects_fxsound_by_name_case_insensitive() {
		assert_eq!(
			Provider::detect("fxsound speakers", ""),
			Some(Provider::FxSound)
		);
	}

	#[test]
	fn detects_fxsound_by_controller() {
		assert_eq!(
			Provider::detect("Speakers", "FxSound Audio Enhancer"),
			Some(Provider::FxSound)
		);
	}

	#[test]
	fn plain_device_is_none() {
		assert_eq!(
			Provider::detect("Speakers (Realtek(R) Audio)", "Realtek"),
			None
		);
	}

	#[test]
	fn target_device_contains_prefix() {
		for p in [Provider::SteelSeriesSonar, Provider::FxSound] {
			assert!(contains_ci(p.target_device(), p.device_prefix()));
		}
	}
}
