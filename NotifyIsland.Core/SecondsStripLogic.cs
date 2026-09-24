using System;

namespace NotifyIsland;

/// <summary>
/// Pure helpers for the FontAudio digital-dot seconds progress strip
/// along the bottom inside edge of the Idle/Collapsed capsule.
/// Progress = fraction of the current minute (0 → 1 as Second 0 → 60).
/// </summary>
public static class SecondsStripLogic
{
    /// <summary>Default number of digital-dot slots across the pill.</summary>
    public const int DefaultSlotCount = 24;

    /// <summary>Minimum / maximum slots when sizing from pill width.</summary>
    public const int MinSlots = 8;
    public const int MaxSlots = 48;

    /// <summary>Approx DIP width reserved per digital-dot (incl. spacing).</summary>
    public const double DotPitchDip = 6.5;

    /// <summary>Horizontal inset so dots sit inside the capsule edge.</summary>
    public const double HorizontalInsetDip = 10;

    /// <summary>Fraction of the current minute [0, 1). Smooth via milliseconds.</summary>
    public static double Progress01(DateTime now)
    {
        var sec = now.Second + now.Millisecond / 1000.0;
        if (sec < 0) sec = 0;
        if (sec >= 60) sec = 59.999;
        return sec / 60.0;
    }

    /// <summary>
    /// How many leading slots are "lit". Uses floor so second 0 → empty,
    /// approaching 60 → all lit (last slot lights near end of minute).
    /// </summary>
    public static int LitCount(double progress01, int slots)
    {
        if (slots <= 0) return 0;
        if (double.IsNaN(progress01) || double.IsInfinity(progress01)) return 0;
        var p = Math.Clamp(progress01, 0.0, 1.0);
        // floor so 0.0 → 0 lit; at p≈1 → slots lit
        var lit = (int)Math.Floor(p * slots);
        if (p >= 0.999) lit = slots;
        return Math.Clamp(lit, 0, slots);
    }

    public static int LitCount(DateTime now, int slots) =>
        LitCount(Progress01(now), slots);

    /// <summary>Slot count from available capsule width (DIP).</summary>
    public static int SlotCountForWidth(double pillWidthDip, double pitchDip = DotPitchDip)
    {
        var pitch = pitchDip <= 0 ? DotPitchDip : pitchDip;
        var usable = Math.Max(0, pillWidthDip - HorizontalInsetDip * 2);
        var n = (int)Math.Floor(usable / pitch);
        return Math.Clamp(n, MinSlots, MaxSlots);
    }
}
