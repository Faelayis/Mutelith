//! Command-line arguments. Unknown arguments are ignored, because older
//! installs and the startup registry entry pass flags this build may not use.

#[derive(Debug, Default, PartialEq, Eq)]
pub struct Args {
	/// `--dev`: log to a console, skip install and auto update.
	pub dev: bool,
	/// `--logs`: also write info messages to the log file.
	pub logs: bool,
	/// `--uninstall`: remove registry entries and the install folder.
	pub uninstall: bool,
	/// `--ghtoken <token>`: GitHub token for the update check.
	pub gh_token: Option<String>,
}

impl Args {
	pub fn parse(args: impl IntoIterator<Item = String>) -> Self {
		let mut parsed = Self::default();
		let mut args = args.into_iter();

		while let Some(arg) = args.next() {
			match arg.to_ascii_lowercase().as_str() {
				"--dev" => parsed.dev = true,
				"--logs" => parsed.logs = true,
				"--uninstall" => parsed.uninstall = true,
				"--ghtoken" => parsed.gh_token = args.next(),
				// `--startup`, `--silent` and anything unknown.
				_ => {}
			}
		}

		parsed
	}
}

#[cfg(test)]
mod tests {
	use super::*;

	fn parse(args: &[&str]) -> Args {
		Args::parse(args.iter().map(|a| a.to_string()))
	}

	#[test]
	fn startup_entry_args() {
		assert_eq!(parse(&["--startup", "--silent"]), Args::default());
	}

	#[test]
	fn all_flags() {
		let args = parse(&["--dev", "--logs", "--UNINSTALL", "--ghtoken", "abc"]);
		assert!(args.dev && args.logs && args.uninstall);
		assert_eq!(args.gh_token.as_deref(), Some("abc"));
	}

	#[test]
	fn ignores_unknown_and_missing_token() {
		let args = parse(&["--whatever", "x", "--ghtoken"]);
		assert_eq!(args, Args::default());
	}
}
