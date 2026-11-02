using System.Runtime.InteropServices;
using System.Text;

namespace DynamicIsland;

/// <summary>Detects games, fullscreen video and presentations on the island's monitor.</summary>
static class FullscreenDetector
{
    public static bool IsActive(IntPtr self)
    {
        // Exclusive fullscreen D3D games, presentation mode, and apps Windows marks as "busy".
        if (SHQueryUserNotificationState(out var state) == 0
            && state is QUNS_BUSY or QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE)
            return true;

        // Borderless fullscreen games and F11 / video fullscreen in browsers.
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == self)
            return false;

        var className = new StringBuilder(64);
        GetClassName(foreground, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd")
            return false;

        var monitor = MonitorFromWindow(foreground, MONITOR_DEFAULTTONEAREST);
        if (monitor != MonitorFromWindow(self, MONITOR_DEFAULTTONEAREST))
            return false;

        // A maximized window with a title bar is not fullscreen, even with an auto-hidden taskbar.
        var style = GetWindowLong(foreground, GWL_STYLE);
        if (IsZoomed(foreground) && (style & WS_CAPTION) == WS_CAPTION)
            return false;

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info) || !GetWindowRect(foreground, out var rect))
            return false;

        var screen = info.rcMonitor;
        return rect.Left <= screen.Left && rect.Top <= screen.Top
            && rect.Right >= screen.Right && rect.Bottom >= screen.Bottom;
    }

    /// <summary>Process name of the window in front, e.g. "VALORANT-Win64-Shipping".</summary>
    public static string? ForegroundProcessName()
    {
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
            return pid == 0 ? null : System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    const int QUNS_BUSY = 2;
    const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    const int QUNS_PRESENTATION_MODE = 4;
    const uint MONITOR_DEFAULTTONEAREST = 2;
    const int GWL_STYLE = -16;
    const int WS_CAPTION = 0x00C00000;

    [StructLayout(LayoutKind.Sequential)]
    struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("shell32.dll")]
    static extern int SHQueryUserNotificationState(out int state);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("user32.dll")]
    static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern int GetWindowLong(IntPtr hWnd, int index);
}
