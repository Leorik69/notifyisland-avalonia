using System;
using System.Collections.Generic;
using System.Linq;

namespace NotifyIsland;

/// <summary>
/// Which process owns the capsule's bottom progress band (1.13). Only one can hold it at a
/// time: a 30 DIP capsule has a single 8 DIP strip, and a second bar drawn over the first
/// reads as a rendering bug rather than as two activities.
/// <para>
/// Priority is deliberate and ordered: clipboard copy is the user's own action in flight
/// and finishes fast, media playback is ambient and long, a timer is deliberate but usually
/// already visible as digits in its monitor row. A loser does not disappear — it stays in
/// the System Monitor row that has its own label and value.
/// </para>
/// </summary>
public enum ProgressBandOwner
{
    None = 0,
    Clipboard,
    Media,
    Timer
}

/// <summary>
/// Inputs the band arbiter needs. Everything is optional: a source that is not running
/// reports <c>false</c> for <see cref="Active"/> and is ignored entirely.
/// </summary>
public sealed class ProgressBandState
{
    /// <summary>A clipboard/file copy is in flight.</summary>
    public bool ClipboardActive { get; set; }
    /// <summary>0..1 for the clipboard copy, clamped on read.</summary>
    public double ClipboardProgress { get; set; }

    /// <summary>SMTC reports a media session.</summary>
    public bool MediaActive { get; set; }
    public double MediaProgress { get; set; }

    /// <summary>A countdown/stopwatch is running or paused-but-not-cancelled.</summary>
    public bool TimerActive { get; set; }
    public bool TimerCountUp { get; set; }
    public double TimerSeconds { get; set; }
    /// <summary>Total length of a countdown, needed for its fraction. Zero for a stopwatch.</summary>
    public double TimerTotalSeconds { get; set; }
}

/// <summary>
/// Pure decision logic for the capsule's progress band (spec:
/// <c>docs/superpowers/specs/2026-09-30--notifyisland-single-capsule.md</c>).
/// <para>
/// This is deliberately not a component: the band is 8 DIP inside a fixed 30 DIP capsule,
/// so the only real question is "who draws here", and the answer has to be testable without
/// an Avalonia dispatcher. Geometry is unchanged by this file — if a change here ever needs
/// a different capsule height, the band is in the wrong place.
/// </para>
/// </summary>
public static class CapsuleProgressBand
{
    /// <summary>Height of the strip the band occupies inside the capsule (DIP).</summary>
    public const double BandH = 8.0;

    /// <summary>Thickness of the drawn bar itself (DIP).</summary>
    public const double BarH = 2.0;

    /// <summary>Who owns the band, by descending priority.</summary>
    public static ProgressBandOwner OwnerOf(ProgressBandState? state)
    {
        if (state is null) return ProgressBandOwner.None;
        if (state.ClipboardActive) return ProgressBandOwner.Clipboard;
        if (state.MediaActive) return ProgressBandOwner.Media;
        if (state.TimerActive) return ProgressBandOwner.Timer;
        return ProgressBandOwner.None;
    }

    /// <summary>True when the band has an owner and the seconds strip must yield.</summary>
    public static bool BandActive(ProgressBandState? state) => OwnerOf(state) != ProgressBandOwner.None;

    /// <summary>
    /// Fraction to draw, 0..1. A source without a meaningful fraction (a stopwatch with no
    /// total, an unknown media duration) reports 0 rather than a fake value: the bar is then
    /// empty but the label still tells the user something is running.
    /// </summary>
    public static double FractionFor(ProgressBandState? state)
    {
        switch (OwnerOf(state))
        {
            case ProgressBandOwner.Clipboard:
                return Clamp01(state!.ClipboardProgress);
            case ProgressBandOwner.Media:
                return Clamp01(state!.MediaProgress);
            case ProgressBandOwner.Timer:
                if (state!.TimerCountUp) return 0;
                var total = state.TimerTotalSeconds;
                if (total <= 0) return 0;
                return Clamp01(1.0 - state.TimerSeconds / total);
            default:
                return 0;
        }
    }

    /// <summary>
    /// Short caption for the band, in Russian, e.g. "Копирование" / "♫" / "05:00". Empty
    /// when there is no owner. Kept here, not in the window, so the wording is unit-tested
    /// like every other string in the project.
    /// </summary>
    public static string LabelFor(ProgressBandState? state)
    {
        switch (OwnerOf(state))
        {
            case ProgressBandOwner.Clipboard:
                return "Копирование";
            case ProgressBandOwner.Media:
                return "♫";
            case ProgressBandOwner.Timer:
                return IslandTimerLogic.FormatRemaining(state!.TimerSeconds);
            default:
                return "";
        }
    }

    /// <summary>
    /// Accent brush key for the band. Charging is deliberately NOT here: the user kept the
    /// battery pill as a real takeover, so a charge bar never competes for this strip.
    /// </summary>
    public static string AccentFor(ProgressBandState? state) => OwnerOf(state) switch
    {
        ProgressBandOwner.Clipboard => "Clipboard",
        ProgressBandOwner.Media => "Media",
        ProgressBandOwner.Timer => "Timer",
        _ => "Idle"
    };

    private static double Clamp01(double v) =>
        double.IsNaN(v) ? 0 : Math.Clamp(v, 0, 1);
}

/// <summary>
/// Row payloads for the System Monitor's status rows (1.13). These are plain data with no
/// platform types, so the window can fill them from SMTC, the timer and the clipboard
/// without the Core learning where any of them come from.
/// </summary>
public sealed class StatusRowModel
{
    /// <summary>Which source the row describes. Drives icon, accent and which actions apply.</summary>
    public StatusRowKind Kind { get; set; } = StatusRowKind.Media;
    /// <summary>Left-hand label, e.g. "Плеер".</summary>
    public string Label { get; set; } = "";
    /// <summary>Right-hand value, e.g. a track title or mm:ss. May be empty.</summary>
    public string Value { get; set; } = "";
    /// <summary>Optional second line under the value (artist).</summary>
    public string Detail { get; set; } = "";
    /// <summary>0..1, or null when the source has no meaningful fraction.</summary>
    public double? Progress { get; set; }
    /// <summary>True when the row should be visible at all.</summary>
    public bool Active { get; set; }
    /// <summary>True when the source is running (vs paused).</summary>
    public bool Playing { get; set; }
}

/// <summary>Which status a monitor row shows. Values are stable — they are persisted.</summary>
public enum StatusRowKind
{
    Media,
    Timer
}
