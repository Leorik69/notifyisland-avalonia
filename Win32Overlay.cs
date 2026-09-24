using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace NotifyIsland;

internal static class Win32Overlay
{
    private const int GwlExstyle = -20;
    private const int WsExNoactivate = 0x08000000;
    private const int WsExToolwindow = 0x00000080;
    private const int WsExTransparent = 0x00000020;
    private const int WsExLayered = 0x00080000;

    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotopmost = new(-2);
    private static readonly IntPtr HwndTop = new(0);
    private static readonly IntPtr HwndBottom = new(1);

    private const uint SwpNosize = 0x0001;
    private const uint SwpNomove = 0x0002;
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpShowwindow = 0x0040;

    private const int MonitorDefaultToNearest = 2;

    // QUERY_USER_NOTIFICATION_STATE
    private const int QunsNotPresent = 1;
    private const int QunsBusy = 2;
    private const int QunsRunningD3dFullScreen = 3;
    private const int QunsPresentationMode = 4;
    private const int QunsAcceptsNotifications = 5;
    private const int QunsQuietTime = 6;
    private const int QunsApp = 7;

    public static void ApplyNoActivate(Window window)
    {
        try
        {
            var hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero) return;
            var ex = GetWindowLong(hwnd, GwlExstyle);
            SetWindowLong(hwnd, GwlExstyle, ex | WsExNoactivate | WsExToolwindow);
        }
        catch (Exception ex)
        {
            AppLog.Warn("ApplyNoActivate failed", ex);
        }
    }

    /// <summary>
    /// Toggle WS_EX_TRANSPARENT click-through (fail-soft). Keeps NOACTIVATE/TOOLWINDOW.
    /// </summary>
    public static void ApplyClickThrough(Window window, bool clickThrough)
    {
        try
        {
            var hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero) return;
            var ex = GetWindowLong(hwnd, GwlExstyle);
            ex |= WsExNoactivate | WsExToolwindow;
            if (clickThrough)
                ex |= WsExTransparent | WsExLayered;
            else
                ex &= ~WsExTransparent;
            SetWindowLong(hwnd, GwlExstyle, ex);
        }
        catch (Exception ex)
        {
            AppLog.Warn("ApplyClickThrough failed", ex);
        }
    }

    /// <summary>
    /// Apply z-order. Win11 limitations: Desktop/BehindApps are best-effort —
    /// DWM, Widgets, and shell Host may reorder; Topmost is the reliable mode.
    /// Call on Opened and whenever ZOrderMode changes.
    /// </summary>
    public static void ApplyZOrder(Window window, ZOrderMode mode)
    {
        try
        {
            var hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero) return;

            // Avalonia Topmost property for the reliable path
            window.Topmost = mode == ZOrderMode.Topmost;

            switch (mode)
            {
                case ZOrderMode.Topmost:
                    SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0,
                        SwpNomove | SwpNosize | SwpNoactivate);
                    break;

                case ZOrderMode.Desktop:
                    // Clear topmost, then insert after desktop WorkerW / Progman so we sit
                    // above icons but typically below normal app windows.
                    SetWindowPos(hwnd, HwndNotopmost, 0, 0, 0, 0,
                        SwpNomove | SwpNosize | SwpNoactivate);
                    var desktop = FindDesktopWorker();
                    if (desktop != IntPtr.Zero)
                    {
                        SetWindowPos(hwnd, desktop, 0, 0, 0, 0,
                            SwpNomove | SwpNosize | SwpNoactivate);
                    }
                    else
                    {
                        // Fallback: bottom of z-order then bump once
                        SetWindowPos(hwnd, HwndBottom, 0, 0, 0, 0,
                            SwpNomove | SwpNosize | SwpNoactivate);
                        SetWindowPos(hwnd, HwndTop, 0, 0, 0, 0,
                            SwpNomove | SwpNosize | SwpNoactivate);
                    }
                    break;

                case ZOrderMode.BehindApps:
                    // Non-topmost + HWND_BOTTOM: below most apps, still above pure desktop wallpaper.
                    SetWindowPos(hwnd, HwndNotopmost, 0, 0, 0, 0,
                        SwpNomove | SwpNosize | SwpNoactivate);
                    SetWindowPos(hwnd, HwndBottom, 0, 0, 0, 0,
                        SwpNomove | SwpNosize | SwpNoactivate | SwpShowwindow);
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("ApplyZOrder failed", ex);
        }
    }

    /// <summary>
    /// Detect exclusive fullscreen / presentation / busy notification state, plus
    /// a best-effort monitor-covering foreground window check. Fail-soft → false.
    /// </summary>
    public static bool IsFullscreenOrBusy()
    {
        try
        {
            if (SHQueryUserNotificationState(out var state) == 0)
            {
                if (state is QunsRunningD3dFullScreen or QunsBusy or QunsPresentationMode)
                    return true;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("SHQueryUserNotificationState failed", ex);
        }

        try
        {
            if (IsForegroundCoveringMonitor())
                return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn("IsForegroundCoveringMonitor failed", ex);
        }

        return false;
    }

    /// <summary>
    /// True when the foreground window covers ≥95% of its monitor and is not
    /// minimized / shell / our process.
    /// </summary>
    private static bool IsForegroundCoveringMonitor()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        if (IsIconic(fg)) return false;

        GetWindowThreadProcessId(fg, out var fgPid);
        if (fgPid == 0) return false;
        if (fgPid == (uint)Environment.ProcessId) return false;

        // Skip desktop / shell
        var className = new char[64];
        var len = GetClassName(fg, className, className.Length);
        if (len > 0)
        {
            var cn = new string(className, 0, len);
            if (cn is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"
                or "XamlExplorerHostIslandWindow" or "Windows.UI.Core.CoreWindow")
                return false;
        }

        if (!GetWindowRect(fg, out var wr)) return false;
        var mon = MonitorFromWindow(fg, MonitorDefaultToNearest);
        if (mon == IntPtr.Zero) return false;
        var mi = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(mon, ref mi)) return false;

        var mw = Math.Max(1, mi.Monitor.Right - mi.Monitor.Left);
        var mh = Math.Max(1, mi.Monitor.Bottom - mi.Monitor.Top);
        var ww = Math.Max(0, wr.Right - wr.Left);
        var wh = Math.Max(0, wr.Bottom - wr.Top);
        var cover = (ww / (double)mw) * (wh / (double)mh);
        // Borderless fullscreen typically ≥0.97; allow a little chrome.
        return cover >= 0.95 && ww >= mw * 0.95 && wh >= mh * 0.95;
    }

    /// <summary>
    /// Find WorkerW (Win10/11 desktop) or Progman. Inserting after this HWND
    /// roughly yields "above icons, below apps" — unreliable on Win11 22H2+ with Widgets.
    /// </summary>
    private static IntPtr FindDesktopWorker()
    {
        var progman = FindWindow("Progman", null);
        var worker = IntPtr.Zero;

        EnumWindows((h, _) =>
        {
            var shell = FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shell != IntPtr.Zero)
            {
                worker = FindWindowEx(IntPtr.Zero, h, "WorkerW", null);
                return false;
            }
            return true;
        }, IntPtr.Zero);

        if (worker != IntPtr.Zero) return worker;
        return progman;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowTitle);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int pquns);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, char[] lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
