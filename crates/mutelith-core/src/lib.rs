//! Core rules for Mutelith: detect the active virtual audio provider and keep
//! Discord muted on every output device except the one viewers should hear.

pub mod audio;
pub mod discord;
pub mod events;
pub mod monitor;
pub mod provider;
pub mod store;
pub mod viewer_echo;

/// Case-insensitive substring match, like C# `OrdinalIgnoreCase` for device names.
pub(crate) fn contains_ci(haystack: &str, needle: &str) -> bool {
	haystack.to_lowercase().contains(&needle.to_lowercase())
}
