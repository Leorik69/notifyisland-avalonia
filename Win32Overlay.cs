using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace NotifyIsland;

internal static class Win32Overlay
{
    private const int GwlExstyle = -20;
    private const int WsExNoactivate = 0x08000000;
    private const int WsExToolwindow = 0x00000080;

    public static void ApplyNoActivate(Window window)
    {
        try
        {
            var hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero) return;
            var ex = GetWindowLong(hwnd, GwlExstyle);
            SetWindowLong(hwnd, GwlExstyle, ex | WsExNoactivate | WsExToolwindow);
        }
        catch
        {
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
