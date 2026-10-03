using System;
using System.Collections.Generic;

namespace NotifyIsland;

/// <summary>One window in the recent-windows list.</summary>
public sealed record RecentWindow
{
    /// <summary>Native window handle. Identity: the same window keeps the same entry.</summary>
    public nint Handle { get; init; }

    /// <summary>Window title as the system reports it. Never empty for a row that is shown.</summary>
    public string Title { get; init; } = "";

    /// <summary>Process name, no extension. Used for the icon and as a fallback label.</summary>
    public string ProcessName { get; init; } = "";

    public DateTimeOffset LastSeen { get; init; }
}

/// <summary>
/// Which processes the window list is allowed to contain.
/// <para>
/// The list is about "what was I just working in", so the filters below all answer the same
/// question: would this row be a place the user could usefully jump back to? A shell window with
/// an empty title has nowhere to send the user, a tool window has no identity, and the island
/// itself must never appear in its own list — clicking a row restores that window, and a row
/// pointing at the island would make the island close itself.
/// </para>
/// </summary>
public static class RecentWindowFilter
{
    /// <summary>Window classes that are chrome rather than content.</summary>
    private static readonly HashSet<string> ToolClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow", "ApplicationFrameWindow_Ghost",
        "ForegroundStaging", "MultitaskingViewFrame",
        "Button", "IME", "MSCTFIME UI", "Default IME", "IME_Window",
    };

    /// <summary>Window styles that mean "this is a tool, a palette, or a menu".</summary>
    public const long GwlStyle = -16;
    public const long GwlExStyle = -20;

    public const long WsVisible = 0x1000_0000;
    public const long WsTool = 0x0000_0080;
    public const long WsCaption = 0x00C0_0000;
    public const long WsChild = 0x4000_0000;
    public const long WsExToolWindow = 0x0000_0080;
    public const long WsExNoActivate = 0x0800_0000;

    /// <summary>Longest title kept. Longer is a document path or a log line, not a window name.</summary>
    public const int MaxTitleLength = 120;

    /// <summary>
    /// Should this window get a row?
    /// <para>
    /// <paramref name="ownProcess"/> is passed in rather than read here: Core has no access to
    /// the process id, and a filter that had to ask the platform would not be a filter.
    /// </para>
    /// </summary>
    public static bool Accept(nint handle, string className, string title, long style, long exStyle,
        int processId, int ownProcess)
    {
        if (handle == IntPtr.Zero) return false;
        if (processId == ownProcess || processId == 0) return false;
        if ((style & WsVisible) == 0) return false;
        if ((style & WsChild) != 0) return false;                 // not a top-level window
        if ((style & WsTool) != 0) return false;                 // a palette, not a window
        if ((exStyle & WsExToolWindow) != 0) return false;       // no taskbar button either
        if ((style & WsCaption) == 0) return false;              // no title bar = no identity
        if (string.IsNullOrWhiteSpace(title)) return false;
        if (title.Length > MaxTitleLength) return false;
        if (ToolClasses.Contains(className ?? "")) return false;
        return true;
    }

    /// <summary>The title as it should be shown: one line, trimmed, length-capped.</summary>
    public static string TitleFor(string? title)
    {
        // "\r\n" is TWO characters, so replacing each one with a space leaves a double space where
        // the line break was — the title came out as "a  b" rather than "a b". Collapsing runs of
        // whitespace afterwards is what makes the result actually read as one line.
        var t = System.Text.RegularExpressions.Regex
            .Replace(title ?? "", @"\s+", " ").Trim();
        if (t.Length <= MaxTitleLength) return t;
        return t[..(MaxTitleLength - 1)].TrimEnd() + "…";
    }
}

/// <summary>
/// The recent-windows list: newest first, one row per window, and a hard cap.
/// <para>
/// MRU (most recently used), not "most recently seen": re-focusing a window you already have in
/// the list must move it to the top rather than add a duplicate, which is what "recent" has to
/// mean for a jump list to be useful.
/// </para>
/// </summary>
public sealed class RecentWindows
{
    /// <summary>Maximum rows. Past this the list is a jump list, not a log.</summary>
    public const int Capacity = 8;

    /// <summary>
    /// A window that has not been seen for this long is dropped on the next update, so the list
    /// reflects the current session rather than everything since the machine booted. 30 minutes is
    /// long enough to cover "I closed it and now want it back" and short enough that yesterday's
    /// windows are gone by morning.
    /// </summary>
    public static readonly TimeSpan Retention = TimeSpan.FromMinutes(30);

    private readonly List<RecentWindow> _items = new();

    public IReadOnlyList<RecentWindow> Items => _items;

    public int Count => _items.Count;

    /// <summary>
    /// Record that this window is in use. Moves it to the front if already known.
    /// <para>
    /// Windows that are no longer valid are dropped here rather than filtered at display time:
    /// a stale handle is dead weight in the list, and the check has to run while the handle is
    /// still known good.
    /// </para>
    /// </summary>
    public void Touch(RecentWindow window, DateTimeOffset now, Func<nint, bool>? isAlive = null)
    {
        if (window.Handle == IntPtr.Zero) return;

        _items.RemoveAll(w =>
        {
            if (w.Handle == window.Handle) return true;
            if (now - w.LastSeen > Retention) return true;
            return isAlive is not null && !isAlive(w.Handle);
        });

        _items.Insert(0, new RecentWindow
        {
            Handle = window.Handle,
            Title = window.Title,
            ProcessName = window.ProcessName,
            LastSeen = now,
        });

        if (_items.Count > Capacity) _items.RemoveRange(Capacity, _items.Count - Capacity);
    }

    /// <summary>Drop everything — used when the list is turned off in settings.</summary>
    public void Clear() => _items.Clear();
}
