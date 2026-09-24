namespace NotifyIsland;

/// <summary>How the main notification appears (Settings → Анимации).</summary>
public enum NotifyAppearStyle
{
    /// <summary>Soft width morph (classic inflate), CubicEaseOut.</summary>
    Inflate,
    /// <summary>Translate Y from −20→0 + fade-in while width morphs.</summary>
    SlideDown,
    /// <summary>Opacity 0→1 + scale 0.85→1 with soft width morph.</summary>
    FadeScale,
    /// <summary>Spring overshoot on width / scale.</summary>
    Bounce,
    /// <summary>Quick scale punch then settle.</summary>
    Pop
}

/// <summary>How the notification dismisses / island collapses.</summary>
public enum NotifyDismissStyle
{
    /// <summary>Soft width shrink.</summary>
    Collapse,
    /// <summary>Translate up + fade-out.</summary>
    SlideUp,
    /// <summary>Opacity 1→0 + scale 1→0.85 while shrinking.</summary>
    FadeScaleOut,
    /// <summary>«Рваный» уход: X jitter while shrinking/fading.</summary>
    Ragged,
    /// <summary>Stepped opacity/offset stutter, then gone.</summary>
    Glitch
}

/// <summary>Soft / spring easing curves for island morph (no linear).</summary>
public static class AnimationEasing
{
    /// <summary>CubicEaseOut — soft settle.</summary>
    public static double CubicOut(double t)
    {
        t = Clamp01(t);
        return 1.0 - Math.Pow(1.0 - t, 3.0);
    }

    /// <summary>CubicEaseInOut.</summary>
    public static double CubicInOut(double t)
    {
        t = Clamp01(t);
        return t < 0.5
            ? 4.0 * t * t * t
            : 1.0 - Math.Pow(-2.0 * t + 2.0, 3.0) / 2.0;
    }

    /// <summary>Damped spring with mild overshoot (Bounce).</summary>
    public static double SpringOut(double t)
    {
        t = Clamp01(t);
        // 1 - e^(-6t) * cos(2.4π t)  → overshoots then settles near 1
        return 1.0 - Math.Exp(-6.0 * t) * Math.Cos(t * Math.PI * 2.4);
    }

    /// <summary>Pop: punch above 1 early, then settle to 1.</summary>
    public static double PopScale(double t)
    {
        t = Clamp01(t);
        if (t < 0.28)
        {
            var u = t / 0.28;
            return 0.88 + 0.30 * CubicOut(u); // → ~1.18
        }
        var v = (t - 0.28) / 0.72;
        return 1.18 - 0.18 * CubicOut(v); // → 1.0
    }

    /// <summary>Glitch: stepped progress (stutter).</summary>
    public static double GlitchStep(double t)
    {
        t = Clamp01(t);
        // Hold plateaus then jump
        if (t < 0.15) return 0.0;
        if (t < 0.28) return 0.22;
        if (t < 0.40) return 0.18; // brief rewind stutter
        if (t < 0.55) return 0.55;
        if (t < 0.70) return 0.48;
        return CubicOut((t - 0.70) / 0.30);
    }

    private static double Clamp01(double t) => t < 0 ? 0 : t > 1 ? 1 : t;
}
