# Mutelith

**Mutelith** fixes

> **Discord / double audio when screen sharing**

on Windows setups that use virtual audio devices such as **SteelSeries Sonar** or **FxSound**, where viewers (or people in the call) sometimes:

-  hear the **game / desktop audio twice**, or
-  hear **their own microphone** coming back with a slight delay.

Mutelith mutes Discord on the output devices that cause the echo, leaves the provider's other virtual devices alone, and unmutes everything when it stops.

## Supported audio stacks

| Provider                                              | Target device (Discord muted)    | Detected by                                         |
| ----------------------------------------------------- | -------------------------------- | --------------------------------------------------- |
| [SteelSeries Sonar](https://steelseries.com/gg/sonar) | `SteelSeries Sonar - Microphone` | Controller `SteelSeries Sonar Virtual Audio Device` |
| [FxSound](https://www.fxsound.com/)                   | `FxSound Speakers`               | Name or controller contains `FxSound`               |

The provider is detected from the **default output device**.

<!-- prettier-ignore -->
> [!WARNING]
> Mutelith is designed and tested for **one active audio stack (provider) at a time**.<br>
> Using several virtual audio providers together (e.g. Sonar + FxSound) may make detection and rules behave incorrectly.

## Install

### App (recommended)

One `.exe`, no runtime. It runs in the tray, starts with Windows, and updates itself.

1. Download [`Mutelith.exe`](https://github.com/Faelayis/Mutelith/releases/latest/download/Mutelith.exe) from [Releases](https://github.com/Faelayis/Mutelith/releases).
2. Run it.

To uninstall, use **Programs and Features**, or run `Mutelith.exe --uninstall`.

### Windhawk mod

For [Windhawk](https://windhawk.net/) users. No separate app.

1. In Windhawk, choose **Create a New Mod**.
2. Replace the code with [`windhawk/mutelith.wh.cpp`](./windhawk/mutelith.wh.cpp).
3. Click **Compile Mod**, then enable it.

> [!NOTE]
> Use one or the other, not both. While `Mutelith.exe` is running, the mod stays idle.

## Rule: `ViewerEcho`

While Discord is running, Mutelith checks the devices as soon as Windows reports a change (default device switched, device added or removed, an app opened a new audio session such as a screen share, someone unmuted a session Mutelith muted), and every 30 seconds as a safety net. If Windows audio notifications cannot be registered, it falls back to checking every 2 seconds. Each check does this:

1. Mute Discord sessions on the provider's **target device** and on every device **outside** the provider (speakers, headphones). Only the mute changes, never the volume.
2. Leave the provider's other virtual devices alone.
3. On stop, or when the default device switches to another provider, unmute Discord on the devices the rule no longer covers.

With FxSound, `FxSound Speakers` is usually the only FxSound device, so every other output device is outside the provider and Discord is muted on all of them, voice chat included.

Discord, Discord PTB, Discord Canary, and Discord Development are supported.

### Windows remembers the mute

The mute is Discord's per-app setting in the Volume Mixer, and Windows saves it for each output device, so it outlives Mutelith. Mutelith records each device it muted Discord on (the app in `HKEY_CURRENT_USER\Software\Mutelith\MutedEndpoints`, the mod in Windhawk's mod storage) and unmutes Discord there once the rule no longer covers it, including after a crash. Windows only allows that while Discord is running and has an audio session on that device. If Discord isn't listed for a device in the Volume Mixer when Mutelith stops, Discord stays muted there until Mutelith runs again while Discord is playing audio on that device, or until you unmute Discord in the Volume Mixer. A mute you set yourself in the Volume Mixer is left alone.

## Project layout

```
crates/
  mutelith-app/    # Mutelith.exe: tray app, install / startup / uninstall, update on startup
  mutelith-core/   # library: Core Audio wrappers, provider detection, Discord lookup, ViewerEcho rule, monitoring loop
  mutelith-update/ # library: auto-update from GitHub releases
  mutelith-cli/    # command-line harness for testing the libraries on a real machine
windhawk/
  mutelith.wh.cpp  # the same rule as a Windhawk tool mod (C++, one file)
```

## Arguments

| Argument            | Effect                                                                                                                                     |
| ------------------- | ------------------------------------------------------------------------------------------------------------------------------------------ |
| `--dev`             | Logs to a console. Skips install and update. Closes only other copies of the same exe.                                                     |
| `--logs`            | Writes all messages to `%LOCALAPPDATA%\Mutelith\logs.txt` (errors are always written). The file is emptied on startup once it passes 1 MB. |
| `--uninstall`       | Unmutes Discord where Mutelith left it muted, removes the startup and Programs and Features entries and the muted device records, then the install folder. |
| `--ghtoken <token>` | GitHub token for the update check.                                                                                                         |

Unknown arguments, such as `--startup` and `--silent` from the startup entry, are ignored.

## Building

Requirements:

-  [Rust](https://rustup.rs/) stable, **MSVC** toolchain (`stable-x86_64-pc-windows-msvc`)
-  [Visual Studio Build Tools](https://visualstudio.microsoft.com/visual-cpp-build-tools/) with the **Desktop development with C++** workload

```bash
cargo build --release
```

```bash
cargo test
```

## Trying it out

Run the tray app from a development build without installing it:

```bash
cargo run -p mutelith-app -- --dev
```

Print the default device, the detected provider, active output devices, and running Discord processes:

```bash
cargo run -p mutelith-cli -- detect
```

Run the monitoring loop. Press **Enter** to stop and restore Discord:

```bash
cargo run -p mutelith-cli -- watch
```

Check GitHub for a newer release. Add `--download` to also download it and verify its SHA-256 (the CLI never installs it):

```bash
cargo run -p mutelith-cli -- update --download
```

Use `--current 0.2.0` to pretend to be an older version, and `--asset <name>` to pick another release file. Set `GITHUB_TOKEN` to raise the GitHub API rate limit.

## Roadmap

-  [ ] Prevent Screen Share echo in other apps

## Contributing

Contributions are welcome!
Fork the repo, open a feature branch from `main`, and submit a pull request with a clear description of your changes.

## License

[MIT](./LICENSE)
