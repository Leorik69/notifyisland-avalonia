using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Threading;

namespace NotifyIsland;

/// <summary>
/// Keeps a list of the windows the user has actually worked in, and can bring one back.
/// <para>
/// Polled rather than hooked: the thing worth noticing is a window becoming the FOREGROUND one,
/// which is a state every window manager already exposes through a shell hook, but installing
/// that hook is a global injection for a list the user looks at occasionally. EnumWindows at a
/// leisurely cadence costs one process walk and answers the same question.
/// </para>
/// <para>
/// What counts as "recent" is <see cref="RecentWindows"/>; this class only reads the system and
/// applies the decision. It is the same split the rest of the project uses, so the rules stay
/// testable without a window manager.
/// </para>
/// </summary>
public sealed class WindowsRecentWindowsSource : IDisposable
{
    /// <summary>Poll period (ms). A jump list does not need to be live.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    private readonly DispatcherTimer _poll;
    private readonly RecentWindows _list = new();
    private bool _disposed;
    private int _ownProcessId = -1;
    private string? _lastForeground;
    private int _restoreFailures;

    public event Action<IReadOnlyList<RecentWindow>>? Changed;

    public RecentWindows List => _list;

    public WindowsRecentWindowsSource()
    {
        _poll = new DispatcherTimer { Interval = PollInterval };
        _poll.Tick += (_, _) => Refresh();
    }

    public void Start()
    {
        try
        {
            // The island's own pid is captured once. Reading Environment.ProcessId per window
            // would be free, but caching it is what makes the filter's "not me" rule a constant
            // rather than a lookup the filter depends on.
            _ownProcessId = Environment.ProcessId;
            Refresh();
            _poll.Start();
        }
        catch (Exception ex)
        {
            AppLog.Warn("WindowsRecentWindowsSource.Start failed — list idle", ex);
        }
    }

    public void Stop()
    {
        try { _poll.Stop(); }
        catch { /* ignore */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    /// <summary>
    /// One pass. Only the FOREGROUND window is recorded.
    /// <para>
    /// Recording every visible window would make the list a snapshot of what is open, which is
    /// not what "recent" means and is already what the taskbar shows. Recording only the window
    /// the user is looking at is the thing the list is for, and it also means one pass does one
    /// cheap check rather than reading every window's title.
    /// </para>
    /// </summary>
    public void Refresh()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;
            if (_lastForeground == hwnd.ToString()) return;   // nothing changed

            var title = ReadTitle(hwnd);
            if (string.IsNullOrWhiteSpace(title)) return;
            var className = ReadClass(hwnd);
            var style = GetWindowLong(hwnd, RecentWindowFilter.GwlStyle);
            var exStyle = GetWindowLong(hwnd, RecentWindowFilter.GwlExStyle);
            GetWindowThreadProcessId(hwnd, out var pid);

            if (!RecentWindowFilter.Accept(hwnd, className, title, style, exStyle, (int)pid, _ownProcessId))
            {
                // Remember the handle even when rejected: re-reading the title of a window we have
                // already decided about is wasted work, and a tool window stays a tool window.
                _lastForeground = hwnd.ToString();
                return;
            }

            _lastForeground = hwnd.ToString();
            var before = _list.Count;
            _list.Touch(new RecentWindow
            {
                Handle = hwnd,
                Title = RecentWindowFilter.TitleFor(title),
                ProcessName = ProcessNameFor((int)pid),
                LastSeen = DateTimeOffset.Now,
            }, DateTimeOffset.Now, IsWindow);
            if (_list.Count != before) Changed?.Invoke(_list.Items);
        }
        catch (Exception ex)
        {
            AppLog.Warn("WindowsRecentWindowsSource.Refresh failed", ex);
        }
    }

    /// <summary>
    /// Bring a window back to the front.
    /// <para>
    /// A window that was minimised has to be restored BEFORE it is activated, or the activation
    /// lands on a minimised window and the user sees nothing happen. SetForegroundWindow is also
    /// refused outright when the calling process does not own the foreground window, which is the
    /// usual case here — so the attach-to-input-threads trick is used, and its failure is counted
    /// rather than logged per attempt, because a user clicking through a stale row would otherwise
    /// fill the log.
    /// </para>
    /// </summary>
    public bool Restore(nint hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return false;
        try
        {
            if (IsIconic(hwnd)) ShowWindow(hwnd, SwRestore);
            var target = GetWindowThreadProcessId(hwnd, out _);
            var current = GetWindowThreadProcessId(GetForegroundWindow(), out _);
            AttachThreadInput(current, target, true);
            try
            {
                BringWindowToTop(hwnd);
                return SetForegroundWindow(hwnd);
            }
            finally
            {
                AttachThreadInput(current, target, false);
            }
        }
        catch (Exception ex)
        {
            if (_restoreFailures++ < 3)
                AppLog.Warn("WindowsRecentWindowsSource.Restore failed", ex);
            return false;
        }
    }

    private static string ProcessNameFor(int pid)
    {
        if (pid <= 0) return "";
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch
        {
            return "";   // exited between enumeration and read
        }
    }

    private const int SwRestore = 9;

    private static string ReadTitle(nint hwnd)
    {
        var len = GetWindowTextLength(hwnd);
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 1);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string ReadClass(nint hwnd)
    {
        var sb = new StringBuilder(256);
        var n = GetClassName(hwnd, sb, sb.Capacity);
        return n > 0 ? sb.ToString() : "";
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hWnd, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int cmd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool attachState);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hWnd, int index);

    private static long GetWindowLong(nint hwnd, long index) =>
        GetWindowLongPtr(hwnd, (int)index).ToInt64();
}
