# Mutelith 🔇

**Mutelith** is a small Windows tray application that fixes

> 🎧 **Discord / double audio when screen sharing**

It is designed for setups that use virtual audio devices such as **SteelSeries Sonar** or **FxSound**, where viewers (or people in the call) sometimes:

- hear the **game / desktop audio twice**, or
- hear **their own microphone** coming back with a slight delay.

Mutelith automatically mutes Discord on the “wrong” devices, keeps it on the correct one, and restores everything when it stops.

## Supported audio stacks 🎛️

| Providers                                             | Rule Support | Status  | Notes |
| ----------------------------------------------------- | ------------ | ------- | ----- |
| [SteelSeries Sonar](https://steelseries.com/gg/sonar) | `ViewerEcho` | ✅ Live | -     |
| [FxSound](https://www.fxsound.com/)                   | `ViewerEcho` | ✅ Live | -     |

<!-- prettier-ignore -->
> [!WARNING]
> Mutelith is currently designed and tested for **one active audio stack (provider) at a time**.  
> Using multiple virtual audio providers together (e.g. Sonar + FxSound at the same time) may cause the detection and rules to behave incorrectly.

## Downloads ⬇️

You’ll usually see **two builds** in the releases:

### [`Mutelith.exe`](https://github.com/Faelayis/Mutelith/releases)

- Larger file size
- Does **not** require any .NET runtime installed (good for “clean” or locked-down machines)
- Useful if you want a **portable** EXE that “just runs” on most Windows 10/11 systems

> **Not sure which one to use?**  
> Try `Mutelith-native.exe` first.  
> If it fails with a “missing .NET” or runtime error, use `Mutelith.exe` instead.

### [`Mutelith-native.exe`](https://github.com/Faelayis/Mutelith/releases)

- Classic **framework-dependent** .NET build
- Smaller file size
- Requires the **.NET 8 Desktop Runtime** to be installed on Windows
- ✅ **Recommended for most users** (especially if you already have other .NET apps installed)

## Contributing 🤝

Contributions are welcome! 🥳  
If you’d like to help, please fork the repo, open a feature branch, and submit a pull request with a clear description of your changes. 💌
