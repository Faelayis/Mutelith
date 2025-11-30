# Mutelith 🔇

**Mutelith** is a small Windows tray application that fixes

> 🎧 **Discord / double audio when screen sharing**

It is designed for setups that use virtual audio devices such as **SteelSeries Sonar** or **FxSound**, where viewers (or people in the call) sometimes:

- hear the **game / desktop audio twice**, or
- hear **their own microphone** coming back with a slight delay.

Mutelith automatically mutes Discord on the “wrong” devices, keeps it on the correct one, and restores everything when it stops.

## Supported audio stacks 🎛️

| Providers                                             | Rule Support                                               | Status  | Notes |
| ----------------------------------------------------- | ---------------------------------------------------------- | ------- | ----- |
| [SteelSeries Sonar](https://steelseries.com/gg/sonar) | [`ViewerEcho`](./Mutelith/Rules/README.md#rule-viewerecho) | ✅ Live | -     |
| [FxSound](https://www.fxsound.com/)                   | [`ViewerEcho`](./Mutelith/Rules/README.md#rule-viewerecho) | ✅ Live | -     |

<!-- prettier-ignore -->
> [!WARNING]
> Mutelith is currently designed and tested for **one active audio stack (provider) at a time**<br>
> Using multiple virtual audio providers together (e.g. Sonar + FxSound at the same time) may cause the detection and rules to behave incorrectly.

## Downloads ⬇️

You’ll usually see **two builds** in the releases:

<!-- prettier-ignore -->
> [!NOTE]
> **Not sure which one to use?**<br>
> Try `Mutelith-native.exe` first.<br>
> If it fails with a “missing .NET” or runtime error, use `Mutelith.exe` instead.

### [`Mutelith.exe`](https://github.com/Faelayis/Mutelith/releases)

- Larger file size
- Does **not** require any .NET runtime installed (good for “clean” or locked-down machines)
- Useful if you want a **portable** EXE that “just runs” on most Windows 10/11 systems

### [`Mutelith-native.exe`](https://github.com/Faelayis/Mutelith/releases)

- Classic **framework-dependent** .NET build
- Smaller file size
- Requires the [**.NET 8 SDK or Desktop Runtime**](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) to be installed on Windows

## Contributing 🤝

Contributions are welcome! 🥳  
If you’d like to help, please fork the repo, open a feature branch, and submit a pull request with a clear description of your changes. 💌
