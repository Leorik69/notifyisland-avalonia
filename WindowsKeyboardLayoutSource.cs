using System;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Threading;

namespace NotifyIsland;

/// <summary>
/// The keyboard layout of whoever is typing, for the island's language flag.
/// <para>
/// Poll, not a hook. A WH_KEYBOARD_LL hook is a global, always-on input interceptor for one
/// cosmetic badge, and this project is not going to install one. Everything needed is already
/// readable: the active layout belongs to the thread of the foreground window, and
/// <c>GetLastInputInfo</c> says how long ago the user touched the keyboard — which is what makes
/// "when you start typing" answerable without guessing from the layout alone.
/// </para>
/// <para>
/// The decision of what to announce is <see cref="KeyboardLayoutTag.Gate"/> in Core, where it is
/// tested. This class only reads Windows.
/// </para>
/// </summary>
public sealed class WindowsKeyboardLayoutSource : IDisposable
{
    private readonly DispatcherTimer _poll;
    private readonly KeyboardLayoutTag.Gate _gate = new();
    private bool _disposed;
    private bool _loggedFirst;

    /// <summary>Raised on the UI thread when a layout worth announcing was observed.</summary>
    public event Action<KeyboardLayoutInfo>? Changed;

    /// <summary>The layout as last read, or null before the first successful read.</summary>
    public KeyboardLayoutInfo? Current { get; private set; }

    public WindowsKeyboardLayoutSource()
    {
        _poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _poll.Tick += (_, _) => Refresh();
    }

    public void Start()
    {
        try
        {
            Refresh();
            _poll.Start();
        }
        catch (Exception ex)
        {
            AppLog.Warn("WindowsKeyboardLayoutSource.Start failed — language flag idle", ex);
        }
    }

    public void Stop()
    {
        try { _poll.Stop(); } catch { /* ignore */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    public void Refresh()
    {
        KeyboardLayoutInfo? observed;
        string trace;
        try
        {
            observed = ReadActive(out trace);
        }
        catch (Exception ex)
        {
            AppLog.Warn("WindowsKeyboardLayoutSource.Refresh failed", ex);
            return;
        }

        if (!_loggedFirst)
        {
            // One line, ever. It also carries the raw trace, because a source that silently
            // reports nothing is indistinguishable from one that was never started.
            _loggedFirst = true;
            AppLog.Info($"WindowsKeyboardLayoutSource: first reading — {trace}");
        }

        Current = observed;
        var shown = _gate.Observe(observed, IdleMs());
        if (shown is not null)
        {
            AppLog.Info($"WindowsKeyboardLayoutSource: announcing {shown.LocaleTag} ({shown.Language})");
            Changed?.Invoke(shown);
        }
    }

    /// <summary>Milliseconds since the last keyboard or mouse input; -1 when it cannot be read.</summary>
    private static int IdleMs()
    {
        try
        {
            var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
            if (!GetLastInputInfo(ref info)) return -1;
            // dwTime is a 32-bit tick count and wraps every 49.7 days. The subtraction has to be
            // UNSIGNED and 32-bit, or the result comes out negative once a week and the gate
            // concludes nobody is ever typing.
            var delta = unchecked((uint)GetTickCount() - info.Tick);
            return delta > int.MaxValue ? int.MaxValue : (int)delta;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>The layout of the foreground window's thread, described.</summary>
    private static KeyboardLayoutInfo? ReadActive(out string trace)
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) { trace = "no foreground window"; return null; }

        var threadId = GetWindowThreadProcessId(fg, out _);
        if (threadId == 0) threadId = GetCurrentThreadId();

        // The HKL's low word is the LANGID of the layout.
        var layout = GetKeyboardLayout(threadId);
        if (layout == IntPtr.Zero) { trace = "no keyboard layout"; return null; }
        var langId = (uint)(layout.ToInt64() & 0xFFFF);

        var tag = ReadLocaleName(langId);
        var info = KeyboardLayoutTag.Describe(tag);
        trace = $"fg=0x{fg.ToInt64():X} tid={threadId} langid=0x{langId:X4} tag='{tag}' " +
                $"describes={(info is null ? "no" : "yes")} idle={IdleMs()}";
        return info;
    }

    private static string? ReadLocaleName(uint langId)
    {
        // LCIDToLocaleName, NOT GetLocaleInfoEx. The two look interchangeable and are not:
        // GetLocaleInfoEx's first parameter is an LPCWSTR locale NAME, and handing it a LangID
        // access-violates — the process dies with 0xC0000005, not an exception anybody can catch.
        // (Measured, not guessed.) This one takes the LangID and hands back the BCP-47 tag.
        var buffer = new StringBuilder(64);
        var written = LCIDToLocaleName(langId, buffer, buffer.Capacity, 0);
        return written > 0 ? buffer.ToString() : null;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    // -- Switching ------------------------------------------------------------------------
    // The island already READS the active layout, so making the badge clickable is a matter of
    // asking Windows for the other one. GetKeyboardLayoutList gives the installed layouts in
    // the system's own order, which is the order the tray and the Win+Space switcher use — so
    // walking it lands on the layout the user would have picked there.

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetKeyboardLayoutList(uint nBuff, IntPtr[]? lpBuff);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadKeyboardLayoutW(string id, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint WmInputLangChangeRequest = 0x0050;

    /// <summary>
    /// Switch to the next installed keyboard layout, the way Win+Space and the tray do.
    /// <para>
    /// Two calls, and both are needed: <c>LoadKeyboardLayout</c> changes the layout of THIS
    /// thread, which on its own would leave every other window on the old one, and the posted
    /// message asks the foreground window's thread to do the same. The result is read back and
    /// verified, so a layout list the user has just emptied reports failure rather than a
    /// silent no-op.
    /// </para>
    /// <para>
    /// Returns the layout's tag, or <c>null</c> when there is nothing to switch to. The caller
    /// uses that to decide whether to make any noise: a click that changed nothing should not
    /// play a sound.
    /// </para>
    /// </summary>
    public string? SwitchToNext()
    {
        try
        {
            // A NULL buffer is how the API is asked how big the list is, so the parameter is
            // declared nullable on purpose — passing an empty array would ask for zero entries
            // and return zero, which reads as "no layouts installed".
            var count = GetKeyboardLayoutList(0, null);
            if (count <= 1) return null;
            var layouts = new IntPtr[count];
            if (GetKeyboardLayoutList(count, layouts) != count) return null;

            var current = GetKeyboardLayout(0);
            var index = Array.IndexOf(layouts, current);
            // A layout list that does not contain the current one (it was just removed) still
            // has a sensible "next": the first entry.
            if (index < 0) index = 0;
            var next = layouts[(index + 1) % layouts.Length];

            // LCIDToLocaleName takes the LOWORD of the HKL, and expects a name-style string —
            // passing a LangID here is the access violation this file's header warns about.
            // It is called BEFORE anything is posted, so a bad locale name aborts the switch
            // rather than half-applying it.
            var lcid = (uint)(next.ToInt64() & 0xFFFF);
            var sb = new StringBuilder(85);
            if (LCIDToLocaleName(lcid, sb, sb.Capacity, 0) <= 0) return null;

            var hwnd = GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
                PostMessageW(hwnd, WmInputLangChangeRequest, IntPtr.Zero, next);
            LoadKeyboardLayoutW(sb.ToString(), 0);

            return sb.ToString();
        }
        catch (Exception ex)
        {
            AppLog.Warn("SwitchToNext failed", ex);
            return null;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "LCIDToLocaleName")]
    private static extern int LCIDToLocaleName(uint locale, StringBuilder localeName, int cchLocaleName, int flags);

    [DllImport("kernel32.dll")]   // kernel32, not user32: user32 has no such export
    private static extern uint GetTickCount();

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo plii);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Tick;
    }
}
