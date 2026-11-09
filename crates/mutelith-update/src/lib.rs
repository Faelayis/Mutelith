//! Auto-update from GitHub releases: find the latest stable release, download
//! the app's asset, verify its SHA-256 digest, and swap the running executable.

use std::fmt;
use std::fs::{self, File};
use std::io::{self, Write};
use std::path::{Path, PathBuf};
use std::process::Command;

use serde::Deserialize;
use sha2::{Digest, Sha256};

pub type Error = Box<dyn std::error::Error + Send + Sync>;
pub type Result<T> = std::result::Result<T, Error>;

/// GitHub repository that publishes Mutelith releases.
pub const REPOSITORY: &str = "Faelayis/Mutelith";
/// Release asset that holds the app executable.
pub const ASSET_NAME: &str = "Mutelith.exe";

const API: &str = "https://api.github.com";
const USER_AGENT: &str = "Mutelith-Updater";

#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord)]
pub struct Version {
	pub major: u64,
	pub minor: u64,
	pub patch: u64,
}

impl Version {
	/// Parse `1.2.3` or `v1.2.3`. Pre-release and build suffixes are rejected.
	pub fn parse(text: &str) -> Option<Self> {
		let text = text.trim();
		let text = text.strip_prefix(['v', 'V']).unwrap_or(text);
		let mut parts = text.split('.').map(|part| part.parse::<u64>().ok());
		let version = Self {
			major: parts.next()??,
			minor: parts.next()??,
			patch: parts.next()??,
		};
		parts.next().is_none().then_some(version)
	}
}

impl fmt::Display for Version {
	fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
		write!(f, "{}.{}.{}", self.major, self.minor, self.patch)
	}
}

#[derive(Debug, Deserialize)]
struct Release {
	tag_name: String,
	body: Option<String>,
	assets: Vec<Asset>,
}

#[derive(Clone, Debug, Deserialize)]
pub struct Asset {
	pub name: String,
	pub size: u64,
	pub browser_download_url: String,
	/// `sha256:<hex>`, computed by GitHub on upload.
	pub digest: Option<String>,
}

/// A release newer than the running version.
#[derive(Clone, Debug)]
pub struct Update {
	pub version: Version,
	pub notes: String,
	pub asset: Asset,
}

pub struct Updater {
	repository: String,
	asset_name: String,
	current: Version,
	token: Option<String>,
}

impl Updater {
	/// `repository` is `owner/name`.
	pub fn new(
		repository: impl Into<String>,
		asset_name: impl Into<String>,
		current: Version,
	) -> Self {
		Self {
			repository: repository.into(),
			asset_name: asset_name.into(),
			current,
			token: None,
		}
	}

	/// GitHub token for the release lookup, to raise the API rate limit.
	pub fn with_token(mut self, token: Option<String>) -> Self {
		self.token = token.filter(|t| !t.trim().is_empty());
		self
	}

	/// The latest stable release, if it is newer than the running version.
	pub fn check(&self) -> Result<Option<Update>> {
		let url = format!("{API}/repos/{}/releases/latest", self.repository);
		let mut request = ureq::get(&url)
			.header("User-Agent", USER_AGENT)
			.header("Accept", "application/vnd.github+json");
		if let Some(token) = &self.token {
			request = request.header("Authorization", &format!("Bearer {token}"));
		}

		let release: Release = request.call()?.body_mut().read_json()?;
		pick_update(release, &self.asset_name, self.current)
	}

	/// Download the update to a temporary file and verify its size and SHA-256.
	/// Returns the path of the verified file.
	pub fn download(&self, update: &Update) -> Result<PathBuf> {
		let expected = expected_sha256(&update.asset)?;
		let path = std::env::temp_dir().join(format!("Mutelith-{}.exe.download", update.version));

		let result = (|| -> Result<()> {
			let mut response = ureq::get(&update.asset.browser_download_url)
				.header("User-Agent", USER_AGENT)
				.header("Accept", "application/octet-stream")
				.call()?;

			let mut writer = HashingWriter::new(File::create(&path)?);
			io::copy(&mut response.body_mut().as_reader(), &mut writer)?;
			let (file, size, actual) = writer.finish();
			file.sync_all()?;

			if size != update.asset.size {
				return Err(format!("downloaded {size} bytes, expected {}", update.asset.size).into());
			}
			if actual != expected {
				return Err(format!("SHA-256 mismatch: got {actual}, expected {expected}").into());
			}
			Ok(())
		})();

		match result {
			Ok(()) => Ok(path),
			Err(err) => {
				let _ = fs::remove_file(&path);
				Err(err)
			}
		}
	}

	/// Download, verify, and replace the running executable with the update.
	/// The new version runs after the caller calls [`relaunch`] and exits.
	pub fn install(&self, update: &Update) -> Result<()> {
		let path = self.download(update)?;
		let result = self_replace::self_replace(&path);
		let _ = fs::remove_file(&path);
		Ok(result?)
	}
}

/// Start the executable at `exe`, usually the path that was just updated.
/// The caller should exit right after.
pub fn relaunch(exe: &Path, args: &[&str]) -> Result<()> {
	Command::new(exe).args(args).spawn()?;
	Ok(())
}

fn pick_update(release: Release, asset_name: &str, current: Version) -> Result<Option<Update>> {
	let version = Version::parse(&release.tag_name)
		.ok_or_else(|| format!("unrecognized release tag: {}", release.tag_name))?;
	if version <= current {
		return Ok(None);
	}

	let asset = release
		.assets
		.into_iter()
		.find(|asset| asset.name.eq_ignore_ascii_case(asset_name))
		.ok_or_else(|| {
			format!(
				"release {} has no asset named {asset_name}",
				release.tag_name
			)
		})?;

	Ok(Some(Update {
		version,
		notes: release.body.unwrap_or_default(),
		asset,
	}))
}

fn expected_sha256(asset: &Asset) -> Result<String> {
	asset
		.digest
		.as_deref()
		.and_then(|digest| digest.strip_prefix("sha256:"))
		.filter(|hex| hex.len() == 64 && hex.bytes().all(|b| b.is_ascii_hexdigit()))
		.map(str::to_ascii_lowercase)
		.ok_or_else(|| {
			format!(
				"asset {} has no SHA-256 digest; refusing to install",
				asset.name
			)
			.into()
		})
}

/// Writes through to `inner` while hashing and counting the bytes.
struct HashingWriter<W> {
	inner: W,
	hasher: Sha256,
	len: u64,
}

impl<W: Write> HashingWriter<W> {
	fn new(inner: W) -> Self {
		Self {
			inner,
			hasher: Sha256::new(),
			len: 0,
		}
	}

	fn finish(self) -> (W, u64, String) {
		let hex = self
			.hasher
			.finalize()
			.iter()
			.map(|b| format!("{b:02x}"))
			.collect();
		(self.inner, self.len, hex)
	}
}

impl<W: Write> Write for HashingWriter<W> {
	fn write(&mut self, buf: &[u8]) -> io::Result<usize> {
		let n = self.inner.write(buf)?;
		self.hasher.update(&buf[..n]);
		self.len += n as u64;
		Ok(n)
	}

	fn flush(&mut self) -> io::Result<()> {
		self.inner.flush()
	}
}

#[cfg(test)]
mod tests {
	use super::*;

	fn v(text: &str) -> Version {
		Version::parse(text).unwrap()
	}

	fn release(tag: &str, assets: &[(&str, Option<&str>)]) -> Release {
		Release {
			tag_name: tag.into(),
			body: Some("notes".into()),
			assets: assets
				.iter()
				.map(|(name, digest)| Asset {
					name: (*name).into(),
					size: 1,
					browser_download_url: format!("https://example.invalid/{name}"),
					digest: digest.map(Into::into),
				})
				.collect(),
		}
	}

	#[test]
	fn parses_versions() {
		assert_eq!(
			v("v0.2.4"),
			Version {
				major: 0,
				minor: 2,
				patch: 4
			}
		);
		assert_eq!(v(" 1.10.0 ").to_string(), "1.10.0");
		assert_eq!(Version::parse("0.3"), None);
		assert_eq!(Version::parse("0.3.0-beta"), None);
		assert_eq!(Version::parse("1.2.3.4"), None);
	}

	#[test]
	fn orders_versions_numerically() {
		assert!(v("0.10.0") > v("0.9.9"));
		assert!(v("1.0.0") > v("0.99.99"));
	}

	#[test]
	fn picks_newer_release_asset_case_insensitive() {
		let update = pick_update(
			release("v0.3.0", &[("mutelith.EXE", None)]),
			ASSET_NAME,
			v("0.2.4"),
		)
		.unwrap()
		.unwrap();
		assert_eq!(update.version, v("0.3.0"));
		assert_eq!(update.asset.name, "mutelith.EXE");
		assert_eq!(update.notes, "notes");
	}

	#[test]
	fn same_or_older_release_is_no_update() {
		let r = || release("v0.3.0", &[(ASSET_NAME, None)]);
		assert!(pick_update(r(), ASSET_NAME, v("0.3.0")).unwrap().is_none());
		assert!(pick_update(r(), ASSET_NAME, v("0.4.0")).unwrap().is_none());
	}

	#[test]
	fn missing_asset_is_error() {
		let r = release("v0.3.0", &[("Mutelith-native.exe", None)]);
		assert!(pick_update(r, ASSET_NAME, v("0.2.4")).is_err());
	}

	#[test]
	fn requires_sha256_digest() {
		let hash = "EBE4B58FF3A79D6E16A28C12610809981B096579CC3967C49E6F2682D39AFBC7";
		let with = |digest: Option<&str>| release("v1.0.0", &[(ASSET_NAME, digest)]).assets.remove(0);

		assert_eq!(
			expected_sha256(&with(Some(&format!("sha256:{hash}")))).unwrap(),
			hash.to_ascii_lowercase()
		);
		assert!(expected_sha256(&with(None)).is_err());
		assert!(expected_sha256(&with(Some("sha1:abc"))).is_err());
		assert!(expected_sha256(&with(Some("sha256:xyz"))).is_err());
	}

	#[test]
	fn hashing_writer_matches_known_sha256() {
		let mut writer = HashingWriter::new(Vec::new());
		writer.write_all(b"abc").unwrap();
		let (data, len, hex) = writer.finish();
		assert_eq!((data.as_slice(), len), (&b"abc"[..], 3));
		assert_eq!(
			hex,
			"ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
		);
	}

	#[test]
	fn parses_github_release_json() {
		let json = r#"{
			"tag_name": "v0.2.4",
			"body": null,
			"prerelease": false,
			"assets": [{
				"name": "Mutelith.exe",
				"size": 154948136,
				"browser_download_url": "https://github.com/Faelayis/Mutelith/releases/download/v0.2.4/Mutelith.exe",
				"digest": "sha256:6e0dc4edbd760d5a72c7a2c60c3cda81393a0ee0ef61f0b82f28fbb1a3c3e6b0"
			}]
		}"#;
		let release = parse_release(json);
		assert_eq!(release.assets[0].size, 154948136);
		assert!(release.body.is_none());
	}

	fn parse_release(json: &str) -> Release {
		let mut body = ureq::Body::builder()
			.mime_type("application/json")
			.data(json.to_owned());
		body.read_json().unwrap()
	}
}
