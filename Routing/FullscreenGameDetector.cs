using Cueway.Config;
using Cueway.Interop;
using Cueway.Media;

namespace Cueway.Routing;

internal static unsafe class FullscreenGameDetector
{
    private static string ResolveExeName(uint pid)
    {
        string? cached = ProcessResolver.TryGetCachedProcessName((int)pid);
        if (!string.IsNullOrEmpty(cached))
            return cached;

        string? exePath = Win32.TryGetProcessImagePath(pid);
        return string.IsNullOrEmpty(exePath) ? string.Empty : Path.GetFileNameWithoutExtension(exePath);
    }

    public static bool IsFullscreenGameActive(AppSettings settings)
    {
        nint fg = Win32.GetForegroundWindow();
        if (fg == 0 || fg == Win32.GetShellWindow())
            return false;

        if (Win32.IsDesktopShellWindowClass(fg))
            return false;

        Win32.GetWindowThreadProcessId(fg, out uint pid);
        if (pid == 0 || pid == Environment.ProcessId)
            return false;

        string? exeName = null;
        if (settings.GameBlacklistExes.Count > 0)
        {
            exeName = ResolveExeName(pid);
            if (!string.IsNullOrEmpty(exeName))
            {
                foreach (string black in settings.GameBlacklistExes)
                {
                    string norm = Path.GetFileNameWithoutExtension(black.Trim());
                    if (!string.IsNullOrEmpty(norm) && exeName.Equals(norm, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }

        if (!Win32.GetWindowRect(fg, out var rect))
            return false;

        nint hMon = Win32.MonitorFromWindow(fg, Win32.MONITOR_DEFAULTTONEAREST);
        if (hMon == 0)
            return false;

        var mi = new Win32.MONITORINFO { cbSize = (uint)sizeof(Win32.MONITORINFO) };
        if (!Win32.GetMonitorInfoW(hMon, &mi))
            return false;

        int monW = mi.rcMonitor.Width;
        int monH = mi.rcMonitor.Height;
        if (monW <= 0 || monH <= 0)
            return false;

        bool coversMonitor =
            rect.Left <= mi.rcMonitor.Left + 2 &&
            rect.Top <= mi.rcMonitor.Top + 2 &&
            rect.Right >= mi.rcMonitor.Right - 2 &&
            rect.Bottom >= mi.rcMonitor.Bottom - 2;

        if (!coversMonitor)
            return false;

        // Distinguish true fullscreen / borderless games from regular maximized desktop windows
        int style = Win32.GetWindowLongW(fg, Win32.GWL_STYLE);
        bool hasCaption = (style & (int)Win32.WS_CAPTION) == (int)Win32.WS_CAPTION;
        bool coversTaskbar =
            mi.rcMonitor.Height > mi.rcWork.Height ||
            mi.rcMonitor.Width > mi.rcWork.Width;

        if (hasCaption && !coversTaskbar)
            return false;

        exeName ??= ResolveExeName(pid);
        if (!string.IsNullOrEmpty(exeName))
        {
            // Do not treat browsers themselves as fullscreen games (e.g. fullscreen YouTube)
            if (ProcessResolver.IsBrowserProcessName(exeName))
                return false;

            // Explorer desktop / shell host is not a game
            if (exeName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}
