using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Mutelith {
	public static class FullscreenDetector {
		[StructLayout(LayoutKind.Sequential)]
		private struct RECT {
			public int Left;
			public int Top;
			public int Right;
			public int Bottom;
		}

		[DllImport("user32.dll")]
		private static extern IntPtr GetForegroundWindow();

		[DllImport("user32.dll")]
		private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

		[DllImport("user32.dll")]
		private static extern bool IsWindowVisible(IntPtr hWnd);

		public static bool IsFullscreenAppActive() {
			try {
				IntPtr hWnd = GetForegroundWindow();
				if (hWnd == IntPtr.Zero) {
					return false;
				}

				if (!IsWindowVisible(hWnd)) {
					return false;
				}

				if (!GetWindowRect(hWnd, out RECT rect)) {
					return false;
				}

				var screen = Screen.FromHandle(hWnd);
				var bounds = screen.Bounds;

				int width = rect.Right - rect.Left;
				int height = rect.Bottom - rect.Top;

				const int tolerance = 2;

				bool matchWidth = Math.Abs(width - bounds.Width) <= tolerance;
				bool matchHeight = Math.Abs(height - bounds.Height) <= tolerance;

				return matchWidth && matchHeight;
			} catch {
				return false;
			}
		}
	}
}