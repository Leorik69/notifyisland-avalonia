using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

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
            var hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero) return;
            var dark = 1;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        }
        catch
        {
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
            var hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero) return;
            var layer = PrefsStore.Current.Layer;
            window.Topmost = layer == "topmost";
            var after = layer switch
            {
                "desktop" => (IntPtr)HwndBottom,
                "normal" => (IntPtr)HwndNotopmost,
                _ => (IntPtr)HwndTopmost
            };
            SetWindowPos(hwnd, after, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpNoactivate);
        }
        catch
        {
        }
    }

    public static void ApplyNoActivate(Window window)
    {
        try
        {
            var hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero) return;

            var ex = GetWindowLongPtr(hwnd, GwlExstyle);
            SetWindowLongPtr(hwnd, GwlExstyle, ex | (IntPtr)(WsExNoactivate | WsExToolwindow | WsExLayered));

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
        catch
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int CxLeftWidth;
        public int CxRightWidth;
        public int CyTopHeight;
        public int CyBottomHeight;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref Margins pMarInset);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
