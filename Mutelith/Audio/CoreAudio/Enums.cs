using System;

namespace Mutelith.Audio.CoreAudio {
	public enum DataFlow {
		Render,
		Capture,
		All
	}

	public enum DeviceState {
		Active = 0x00000001,
		Disabled = 0x00000002,
		NotPresent = 0x00000004,
		Unplugged = 0x00000008,
		All = 0x0000000F
	}

	public enum Role {
		Console,
		Multimedia,
		Communications
	}

	public enum AudioSessionState {
		Inactive = 0,
		Active = 1,
		Expired = 2
	}
}