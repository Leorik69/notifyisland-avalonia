using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace NotifyIsland;

/// <summary>
/// Writes text and file-drop payloads back to the Windows clipboard using Win32
/// user32!OpenClipboard + SetClipboardData. Companion to <see cref="WindowsClipboardSource"/>,
/// which only reads. Used by the tray submenu's "re-copy last item" affordance.
///
/// Returns <c>false</c> on any failure (locked clipboard, marshalling error, etc.) so the
/// caller can refresh the icon without surfacing a dialog.
/// </summary>
public static class WindowsClipboardWriter
{
    public const uint CF_UNICODETEXT = 13;
    public const uint CF_HDROP = 15;

    /// <summary>Put a UTF-16 string on the clipboard. Empty string is treated as a no-op clear.</summary>
    public static bool WriteText(string text)
    {
        if (text is null) text = "";
        if (!OpenClipboard(IntPtr.Zero)) return false;
        try
        {
            EmptyClipboard();
            if (text.Length == 0) return true;

            // Bytes for UTF-16 + null terminator. GHND = movable + zeroinit (0x0042).
            var bytes = checked((text.Length + 1) * 2);
            var hGlobal = GlobalAlloc(0x0042, (uint)bytes);
            if (hGlobal == IntPtr.Zero) return false;
            try
            {
                var ptr = GlobalLock(hGlobal);
                if (ptr == IntPtr.Zero) return false;
                try
                {
                    Marshal.Copy(System.Text.Encoding.Unicode.GetBytes(text + "\0"), 0, ptr, bytes);
                }
                finally { GlobalUnlock(hGlobal); }
                if (SetClipboardData(CF_UNICODETEXT, hGlobal) == IntPtr.Zero)
                    return false;
                // hGlobal is now owned by the clipboard; do not free it.
                return true;
            }
            catch
            {
                GlobalFree(hGlobal);
                throw;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("WriteText failed", ex);
            return false;
        }
        finally { CloseClipboard(); }
    }

    /// <summary>
    /// Put a file drop (one or more paths) on the clipboard. Layout matches the standard
    /// DROPFILES structure so Explorer / FileExplorer / FileDialogs recognize it.
    /// </summary>
    public static bool WriteFiles(IReadOnlyList<string> paths)
    {
        if (paths is null || paths.Count == 0) return false;
        // Reject any path that's null/empty/whitespace before allocating.
        for (var i = 0; i < paths.Count; i++)
            if (string.IsNullOrWhiteSpace(paths[i])) return false;

        if (!OpenClipboard(IntPtr.Zero)) return false;
        try
        {
            EmptyClipboard();

            // DROPFILES: DWORD (pt.x) = 0, DWORD (pt.y) = 0, DWORD fNC = 0, DWORD fWide = 1,
            // then a sequence of double-null-terminated UTF-16 file names, then a final null wchar.
            var drop = new DROPFILES { pt = new POINT { x = 0, y = 0 }, fNC = 0, fWide = 1 };
            var dropSize = Marshal.SizeOf<DROPFILES>();
            // names length (incl trailing null wchars) in bytes
            int nameBytes = 0;
            for (var i = 0; i < paths.Count; i++)
                nameBytes += (paths[i].Length + 1) * 2;
            nameBytes += 2; // final terminator

            var totalBytes = checked(dropSize + nameBytes);
            var hGlobal = GlobalAlloc(0x0042, (uint)totalBytes);
            if (hGlobal == IntPtr.Zero) return false;
            try
            {
                var ptr = GlobalLock(hGlobal);
                if (ptr == IntPtr.Zero) return false;
                try
                {
                    Marshal.StructureToPtr(drop, ptr, false);
                    var cursor = ptr + dropSize;
                    for (var i = 0; i < paths.Count; i++)
                    {
                        var w = System.Text.Encoding.Unicode.GetBytes(paths[i] + "\0");
                        Marshal.Copy(w, 0, cursor, w.Length);
                        cursor += w.Length;
                    }
                    // final null terminator
                    Marshal.WriteInt16(cursor, 0);
                }
                finally { GlobalUnlock(hGlobal); }
                if (SetClipboardData(CF_HDROP, hGlobal) == IntPtr.Zero)
                    return false;
                return true;
            }
            catch
            {
                GlobalFree(hGlobal);
                throw;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("WriteFiles failed", ex);
            return false;
        }
        finally { CloseClipboard(); }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, uint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DROPFILES
    {
        public POINT pt;
        public int fNC;
        public int fWide;
    }
}