# Rule: `ViewerEcho`

`ViewerEcho` is an abstract rule that fixes:

### Logic

- It **mutes Discord on the wrong audio devices** (where it causes echo)
- It **keeps Discord on the “main” device** that should be heard
- It **saves the original volumes/mute states** and can restore them later

So when the rule is active, viewers no longer hear **double / echo audio** from Discord.

### What problem does it solve?

Typical screen-share setup:

- You have multiple output devices:
   - Speakers / headphones
   - Virtual devices (FxSound, SteelSeries Sonar, etc.)
- Discord may receive audio from more than one signal path  
  → Viewers hear **echo / double audio** in the stream.
