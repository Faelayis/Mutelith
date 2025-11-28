namespace Mutelith {
	public class AudioSessionConfig {
		public string DeviceName { get; set; } = string.Empty;
		public string ProcessName { get; set; } = string.Empty;
		public uint ProcessId { get; set; }
		public float Volume { get; set; }
		public bool IsMuted { get; set; }
	}
}