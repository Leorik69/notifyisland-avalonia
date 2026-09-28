using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace NotifyIsland;

/// <summary>
/// Polls the Windows clipboard for changes. Fires <see cref="Captured"/> when a new
/// text or file item lands. Pure-Windows (Win32 user32); reflection-free.
///
/// Avoids the <c>AddClipboardFormatListener</c> route because that needs a HWND we
/// don't otherwise own — the polling approach is one Win32 call + open/read/close
/// when the sequence bumps. Fine on a 1s timer.
///
/// Images are out of scope for v1 (per research-modules.md §Clipboard v1.1).
/// </summary>
public sealed class WindowsClipboardSource : IDisposable
{
    public const uint CF_UNICODETEXT = 13;
    public const uint CF_HDROP = 15;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
    public bool IsRunning { get; private set; }

    /// <summary>Raised when a new text/file clipboard item is captured. Handler runs on UI thread.</summary>
    public event Action<ClipboardEntry>? Captured;

    private readonly ClipboardHistory _history;
    private readonly Action<Action> _postToUi;
    private uint _lastSequence;
    private readonly System.Threading.Timer _timer;

    public WindowsClipboardSource(ClipboardHistory history, Action<Action> postToUi)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _postToUi = postToUi ?? throw new ArgumentNullException(nameof(postToUi));
        _timer = new System.Threading.Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
        _lastSequence = GetClipboardSequenceNumber();
    }

    public void Start()
    {
        if (IsRunning) return;
        IsRunning = true;
        _lastSequence = GetClipboardSequenceNumber();
        _timer.Change(PollInterval, PollInterval);
        AppLog.Info($"WindowsClipboardSource started (poll={PollInterval.TotalSeconds:F1}s, baseline seq={_lastSequence})");
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        AppLog.Info("WindowsClipboardSource stopped");
    }

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
    }

    private void Tick()
    {
        var seq = GetClipboardSequenceNumber();
        if (seq == 0)
        {
            AppLog.Warn("clipboard GetClipboardSequenceNumber returned 0 — last-error access denied?");
            return;
        }
        if (seq == _lastSequence) return;
        AppLog.Info($"clipboard sequence changed {_lastSequence} -> {seq}");
        _lastSequence = seq;
        var entry = TryCapture();
        if (entry is null)
        {
            AppLog.Info("clipboard sequence changed but TryCapture returned null (locked or no data?)");
            return;
        }
        AppLog.Info($"clipboard captured: kind={entry.Kind} textLen={entry.Text?.Length ?? 0} paths={entry.Paths?.Count ?? 0}");
        _postToUi(() =>
        {
            _history.Push(entry);
            Captured?.Invoke(entry);
            AppLog.Info($"clipboard pushed to history: count={_history.Count}");
        });
    }

    private ClipboardEntry? TryCapture()
    {
        // Order matters: CF_HDROP first because a copy of an Explorer file shows as both
        // CF_HDROP and a "file path as text" on some builds. We want the file interpretation.
        if (TryReadFiles(out var paths) && paths is { Count: > 0 })
            return paths.Count == 1
                ? ClipboardEntry.FromFile(paths[0], DateTimeOffset.UtcNow)
                : ClipboardEntry.FromFiles(paths, DateTimeOffset.UtcNow);
        if (TryReadText(out var text))
            return ClipboardEntry.FromText(text, DateTimeOffset.UtcNow);
        return null;
    }

    private static bool TryReadText(out string text)
    {
        text = "";
        if (!OpenClipboard(IntPtr.Zero)) return false;
        try
        {
            if (!IsClipboardFormatAvailable(CF_UNICODETEXT)) return false;
            var hGlobal = GetClipboardData(CF_UNICODETEXT);
            if (hGlobal == IntPtr.Zero) return false;
            var ptr = GlobalLock(hGlobal);
            if (ptr == IntPtr.Zero) return false;
            try { text = Marshal.PtrToStringUni(ptr) ?? ""; }
            finally { GlobalUnlock(hGlobal); }
            return text.Length > 0;
        }
        catch (Exception ex) { AppLog.Warn("clipboard text read failed", ex); return false; }
        finally { CloseClipboard(); }
    }

    private static bool TryReadFiles(out IReadOnlyList<string> paths)
    {
        paths = Array.Empty<string>();
        if (!OpenClipboard(IntPtr.Zero)) return false;
        try
        {
            if (!IsClipboardFormatAvailable(CF_HDROP)) return false;
            var hDrop = GetClipboardData(CF_HDROP);
            if (hDrop == IntPtr.Zero) return false;
            var count = NI_DragQueryFileCount(hDrop);
            if (count == 0 || count > 1024) return false;
            var list = new List<string>((int)count);
            for (uint i = 0; i < count; i++)
            {
                var size = NI_DragQueryFileLen(hDrop, i);
                if (size <= 0) continue;
                var sb = new System.Text.StringBuilder((int)size + 1);
                if (NI_DragQueryFileStr(hDrop, i, sb, (uint)sb.Capacity) > 0)
                    list.Add(sb.ToString());
            }
            paths = list;
            return list.Count > 0;
        }
        catch (Exception ex) { AppLog.Warn("clipboard files read failed", ex); return false; }
        finally { CloseClipboard(); }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    // Three thin wrappers around shell32!DragQueryFileW. Renamed (NI_ prefix) so they
    // don't collide with the .NET 8 BCL extension method of the same name.
    [DllImport("shell32.dll", EntryPoint = "DragQueryFileW")]
    private static extern uint NI_DragQueryFileCount(IntPtr hDrop);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "DragQueryFileW")]
    private static extern uint NI_DragQueryFileLen(IntPtr hDrop, uint iFile);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "DragQueryFileW")]
    private static extern uint NI_DragQueryFileStr(IntPtr hDrop, uint iFile, [Out] System.Text.StringBuilder lpszFile, uint cch);
}
