namespace NotifyIsland;

/// <summary>User-facing animation speed for island morph / pulse / breath.</summary>
public enum AnimationSpeed
{
    /// <summary>No motion — instant layout, no pulse/breath.</summary>
    Off,
    /// <summary>~1.6× base durations (slower, softer).</summary>
    Slow,
    /// <summary>Base token durations (default).</summary>
    Normal,
    /// <summary>~0.55× base durations (snappier).</summary>
    Fast
}

/// <summary>Maps <see cref="AnimationSpeed"/> to duration multipliers and scaled ms.</summary>
public static class AnimationTiming
{
    public const double SlowMultiplier = 1.6;
    public const double NormalMultiplier = 1.0;
    public const double FastMultiplier = 0.55;

    /// <summary>Base unread-dot pulse full cycle (ms) at Normal speed.</summary>
    public const int PulsePeriodMs = 1600;

    /// <summary>Base idle breathing full cycle (ms) at Normal speed.</summary>
    public const int BreathPeriodMs = 3200;

    public static double Multiplier(AnimationSpeed speed) => speed switch
    {
        AnimationSpeed.Off => 0,
        AnimationSpeed.Slow => SlowMultiplier,
        AnimationSpeed.Fast => FastMultiplier,
        _ => NormalMultiplier
    };

    public static bool IsEnabled(AnimationSpeed speed) => speed != AnimationSpeed.Off;

    /// <summary>
    /// Scale a token duration. Off → 1 ms (instant); otherwise round(base×mult), min 1.
    /// </summary>
    public static int ScaleMs(int baseMs, AnimationSpeed speed)
    {
        var m = Multiplier(speed);
        if (m <= 0) return 1;
        return Math.Max(1, (int)Math.Round(baseMs * m));
    }
}
