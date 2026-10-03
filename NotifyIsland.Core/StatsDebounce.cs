namespace NotifyIsland;

/// <summary>
/// Hysteresis for sampled metrics so the pill does not flicker on sub-threshold jitter.
/// Pure static helpers; the Windows source is their only production caller.
/// </summary>
public static class StatsDebounce
{
    /// <summary>
    /// Returns <paramref name="previous"/> when |next - previous| is below
    /// <paramref name="thresholdPct"/> percentage points; otherwise returns <paramref name="next"/>.
    /// </summary>
    public static double Percent(double previous, double next, double thresholdPct)
        => Math.Abs(next - previous) < thresholdPct ? previous : next;

    /// <summary>
    /// Byte-rate variant: reuses <paramref name="previous"/> when the absolute delta is below
    /// <paramref name="thresholdBytesPerSec"/>. Never returns a negative rate.
    /// </summary>
    public static long Rate(long previous, long next, long thresholdBytesPerSec)
        => Math.Abs(next - previous) < thresholdBytesPerSec ? previous : Math.Max(0, next);
}
