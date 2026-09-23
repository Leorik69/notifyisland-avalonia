using System;

namespace NotifyIsland;

/// <summary>
/// Pure helpers for island countdown / stopwatch (no WinRT).
/// Playing on OverlayPayload = running; false = paused.
/// CountUp on OverlayPayload = stopwatch mode.
/// </summary>
public static class IslandTimerLogic
{
    public static readonly int[] PresetMinutes = [1, 5, 10, 25];

    public const int DefaultPresetMinutes = 5;
    public const int MinPresetMinutes = 1;
    public const int MaxPresetMinutes = 180;
    public const int PriorityReclaimSeconds = 60;

    public static int ClampPresetMinutes(int minutes) =>
        Math.Clamp(minutes <= 0 ? DefaultPresetMinutes : minutes, MinPresetMinutes, MaxPresetMinutes);

    public static OverlayPayload CountdownPayload(int totalSeconds, string? title = null) => new()
    {
        Title = string.IsNullOrWhiteSpace(title) ? "Таймер" : title.Trim(),
        RemainingSeconds = Math.Max(0, totalSeconds),
        Playing = true,
        CountUp = false
    };

    public static OverlayPayload StopwatchPayload(string? title = null) => new()
    {
        Title = string.IsNullOrWhiteSpace(title) ? "Секундомер" : title.Trim(),
        RemainingSeconds = 0,
        Playing = true,
        CountUp = true
    };

    public static OverlayPayload CompletedPayload() => new()
    {
        Title = "Таймер",
        Body = "Время вышло",
        RemainingSeconds = 0,
        Playing = false,
        CountUp = false
    };

    /// <summary>mm:ss or h:mm:ss when ≥1 hour.</summary>
    public static string FormatRemaining(double seconds)
    {
        var total = (int)Math.Ceiling(Math.Max(0, seconds));
        var h = total / 3600;
        var m = (total % 3600) / 60;
        var s = total % 60;
        if (h > 0)
            return $"{h}:{m:00}:{s:00}";
        return $"{m:00}:{s:00}";
    }

    public static int PresetToSeconds(int minutes) =>
        ClampPresetMinutes(minutes) * 60;

    /// <summary>
    /// Running/paused timer owns the island over SMTC until cancel/complete,
    /// unless the user explicitly opened Media (click path).
    /// </summary>
    public static bool TimerOwnsIsland(OverlayKind kind, bool userOpenedMedia) =>
        !userOpenedMedia && kind == OverlayKind.Timer;

    public static bool ShouldCompleteCountdown(OverlayKind kind, OverlayPayload payload) =>
        kind == OverlayKind.Timer
        && !payload.CountUp
        && payload.Playing
        && payload.RemainingSeconds <= 0;
}
