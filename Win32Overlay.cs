using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using NotifyIsland.Core;

namespace NotifyIsland;

internal static class Win32Overlay
{
    private const int GwlExstyle = -20;
    private const int WsExNoactivate = 0x08000000;
    private const int WsExToolwindow = 0x00000080;
    private const int WsExLayered = 0x00080000;

    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmWcpDonotround = 1;
    private const int DwmSbtNone = 1;
    private const int DwmwaColorNone = unchecked((int)0xFFFFFFFE);

    public static void ApplyNormalChrome(Window window)
    {
        try
        {
            var hwnd = window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
            if (hwnd == nint.Zero) return;
            var dark = 1;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        }
        catch (Exception ex)
        {
            IslandLog.Write("win32", "chrome " + ex.Message);
        }
    }

    private const int HwndTopmost = -1;
    private const int HwndNotopmost = -2;
    private const int HwndBottom = 1;
    private const uint SwpNomove = 0x0002;
    private const uint SwpNosize = 0x0001;
    private const uint SwpNoactivate = 0x0010;

    public static void ApplyLayer(Window window)
    {
        try
        {
            var hwnd = window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
            if (hwnd == nint.Zero) return;
            var layer = PrefsStore.Current.Layer;
            window.Topmost = layer == "topmost";
            var after = layer switch
            {
                "desktop" => (nint)HwndBottom,
                "normal" => (nint)HwndNotopmost,
                _ => (nint)HwndTopmost
            };
            SetWindowPos(hwnd, after, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpNoactivate);
        }
        catch (Exception ex)
        {
            IslandLog.Write("win32", "layer " + ex.Message);
        }
    }

    public static void ApplyNoActivate(Window window)
    {
        try
        {
            var hwnd = window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
            if (hwnd == nint.Zero) return;

            var ex = GetWindowLongPtrSafe(hwnd, GwlExstyle);
            SetWindowLongPtrSafe(hwnd, GwlExstyle, ex | (nint)(WsExNoactivate | WsExToolwindow | WsExLayered));

            var margins = new Margins { CxLeftWidth = -1, CxRightWidth = -1, CyTopHeight = -1, CyBottomHeight = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);

            var noRound = DwmWcpDonotround;
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref noRound, sizeof(int));

            var noBackdrop = DwmSbtNone;
            DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref noBackdrop, sizeof(int));

            var noBorder = DwmwaColorNone;
            DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref noBorder, sizeof(int));

            var dark = 1;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
            ApplyLayer(window);
        }
        catch (Exception ex)
        {
            IslandLog.Write("win32", "noactivate " + ex.Message);
        }
    }

    private static nint GetWindowLongPtrSafe(nint hwnd, int index)
    {
        Marshal.SetLastPInvokeError(0);
        var v = GetWindowLongPtr(hwnd, index);
        var err = Marshal.GetLastWin32Error();
        if (v == nint.Zero && err != 0)
            IslandLog.Write("win32", "GetWindowLongPtr err=" + err);
        return v;
    }

    private static nint SetWindowLongPtrSafe(nint hwnd, int index, nint value)
    {
        Marshal.SetLastPInvokeError(0);
        var v = SetWindowLongPtr(hwnd, index, value);
        var err = Marshal.GetLastWin32Error();
        if (v == nint.Zero && err != 0)
            IslandLog.Write("win32", "SetWindowLongPtr err=" + err);
        return v;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int CxLeftWidth;
        public int CxRightWidth;
        public int CyTopHeight;
        public int CyBottomHeight;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint hWnd, ref Margins pMarInset);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);
}
