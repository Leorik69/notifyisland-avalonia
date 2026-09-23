using System;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls;

namespace NotifyIsland;

internal static class Win32Overlay
{
    private const int GwlExstyle = -20;
    private const int WsExNoactivate = 0x08000000;
    private const int WsExToolwindow = 0x00000080;

    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotopmost = new(-2);
    private static readonly IntPtr HwndTop = new(0);
    private static readonly IntPtr HwndBottom = new(1);

    private const uint SwpNosize = 0x0001;
    private const uint SwpNomove = 0x0002;
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpShowwindow = 0x0040;

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
}
