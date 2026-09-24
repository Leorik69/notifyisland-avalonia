using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using WinFormsClipboard = System.Windows.Forms.Clipboard;

namespace NotifyIsland;

/// <summary>
/// Thin wrapper around <see cref="System.Windows.Forms.Clipboard"/> for writing to
/// the system clipboard from the Avalonia UI thread (STA). Fail-soft: every call
/// returns success/failure, never throws. Use for click-to-restore from history.
/// </summary>
public static class WindowsClipboardWriter
{
    public const uint CF_UNICODETEXT = 13;
    public const uint CF_HDROP = 15;

    /// <summary>Returns true on success; false if WinForms clipboard rejected the write.</summary>
    public static bool WriteText(string text)
    {
        if (text is null) return false;
        try
        {
            // WinFormsClipboard.SetText requires an STA thread. Avalonia's UI thread is STA.
            VerifySta();
            WinFormsClipboard.SetText(text);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn("WriteText failed", ex);
            return false;
        }
    }

    /// <summary>Replaces clipboard with the given file paths (Explorer-style file drop list).</summary>
    public static bool WriteFiles(IReadOnlyList<string> paths)
    {
        if (paths is null || paths.Count == 0) return false;
        try
        {
            VerifySta();
            var col = new System.Collections.Specialized.StringCollection();
            foreach (var p in paths) col.Add(p ?? "");
            WinFormsClipboard.SetFileDropList(col);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn("WriteFiles failed", ex);
            return false;
        }
    }

    private static void VerifySta()
    {
        var a = Thread.CurrentThread.GetApartmentState();
        if (a != ApartmentState.STA)
            throw new InvalidOperationException(
                $"Clipboard write requires STA thread; current apartment is {a}. " +
                "Marshal to the UI thread via Avalonia.Threading.Dispatcher.UIThread.Post.");
    }
}
