namespace NotifyIsland;

/// <summary>Which mirror movement a Meteocons key plays. See <see cref="MeteoconsMotionTrack"/>.</summary>
public enum MeteoconsMotionKind
{
    /// <summary>Not a Meteocons key, or a key with no mirrored motion — the icon stays put.</summary>
    None,
    /// <summary>Full 360° turn, linear. clear / partly.</summary>
    Spin,
    /// <summary>Soft vertical bob. cloud / drizzle / rain / snow / sleet.</summary>
    Bob,
    /// <summary>Opacity pulse. storm / fog.</summary>
    Pulse,
}

/// <summary>One rendered frame: the three values the app layer writes onto the icon host.</summary>
/// <param name="Angle">Rotation in degrees; 0 for every kind but <see cref="MeteoconsMotionKind.Spin"/>.</param>
/// <param name="OffsetY">Vertical offset in DIP; 0 for every kind but <see cref="MeteoconsMotionKind.Bob"/>.</param>
/// <param name="Opacity">Host opacity; 1 for every kind but <see cref="MeteoconsMotionKind.Pulse"/>.</param>
public readonly record struct MeteoconsFrame(double Angle, double OffsetY, double Opacity);

/// <summary>
/// The frame maths of the Meteocons mirror motion, as a pure function of elapsed time.
///
/// Why this is in Core and not in <c>MeteoconsMotion</c>: the app-layer file used to build an
/// <c>Avalonia.Animation.Animation</c> per key and call <c>RunAsync</c> on it. Two things were
/// wrong with that, and both were invisible until the log caught them —
/// <list type="bullet">
/// <item>the only public <c>RunAsync</c> in Avalonia 11.3 takes the control to animate, so
/// <c>anim.RunAsync(_pillScale)</c> type-checks and then throws <c>InvalidCastException</c>
/// inside Avalonia when it casts the <c>ScaleTransform</c> to a <c>Visual</c>; and</item>
/// <item>that same overload faults its returned Task for <see cref="MeteoconsMotionKind"/>-style
/// <c>IterationCount.Infinite</c> animations with «Looping animations must not use the Run
/// method.» — a fire-and-forget call turned that into a permanent unobserved-task-exception
/// entry in the log and, on top of that, no motion at all.</item>
/// </list>
///
/// So the movement is a hand-ticked function of elapsed milliseconds and the easing comes from
/// <see cref="AnimEase"/>, like the rest of the 1.12.4 layer. Keeping it pure here is also what
/// makes it testable: the test project references Core only and cannot build an Avalonia window.
///
/// The shape is deliberately IDENTICAL to the keyframes it replaces — see <see cref="Swing"/> for
/// why the easing is applied to the whole cycle before the triangle is walked, and not to each
/// half separately.
/// </summary>
public static class MeteoconsMotionTrack
{
    /// <summary>
    /// The movement a weather key mirrors. <see cref="MeteoconsMotionKind.None"/> for anything
    /// that is not one of the nine Meteocons keys.
    /// </summary>
    public static MeteoconsMotionKind KindFor(string? key) => key?.Trim().ToLowerInvariant() switch
    {
        "weather-clear" => MeteoconsMotionKind.Spin,
        "weather-partly" => MeteoconsMotionKind.Spin,
        "weather-cloud" or "weather-drizzle" or "weather-rain"
            or "weather-snow" or "weather-sleet" => MeteoconsMotionKind.Bob,
        "weather-storm" or "weather-fog" => MeteoconsMotionKind.Pulse,
        _ => MeteoconsMotionKind.None
    };

    /// <summary>
    /// Length of one full cycle in ms — 0 for <see cref="MeteoconsMotionKind.None"/>, where
    /// there is nothing to time. The two spins differ because the clear icon is a plain sun
    /// (6 s) and the partly-cloudy one has to stay readable behind a cloud (10 s).
    /// </summary>
    public static int PeriodMsFor(string? key) => key?.Trim().ToLowerInvariant() switch
    {
        "weather-clear" => OverlayTokens.MeteoconsSpinClearMs,
        "weather-partly" => OverlayTokens.MeteoconsSpinPartlyMs,
        "weather-cloud" or "weather-drizzle" or "weather-rain"
            or "weather-snow" or "weather-sleet" => OverlayTokens.MeteoconsBobMs,
        "weather-storm" => OverlayTokens.MeteoconsPulseStormMs,
        "weather-fog" => OverlayTokens.MeteoconsPulseFogMs,
        _ => 0
    };

    /// <summary>
    /// The opacity trough a pulse key dips to; 1 for the kinds that do not touch opacity.
    /// </summary>
    public static double PulseMinFor(string? key) => key?.Trim().ToLowerInvariant() switch
    {
        "weather-storm" => OverlayTokens.MeteoconsPulseStormMin,
        "weather-fog" => OverlayTokens.MeteoconsPulseFogMin,
        _ => 1.0
    };

    /// <summary>
    /// The frame at <paramref name="elapsedMs"/> into an endless <paramref name="periodMs"/> cycle.
    ///
    /// Time is wrapped, not accumulated: a tick that arrives late or a host that re-attaches
    /// after an hour must land on the same value the steady clock would have produced, or the
    /// icon would visibly jump.
    /// </summary>
    public static MeteoconsFrame FrameAt(MeteoconsMotionKind kind, string? key, int periodMs, double elapsedMs)
    {
        if (kind == MeteoconsMotionKind.None || periodMs <= 0) return new MeteoconsFrame(0, 0, 1);

        var t = Wrap01(elapsedMs / periodMs);
        return kind switch
        {
            // Linear on purpose: the upstream SMIL turns the sun at a constant rate, and a spin
            // that eased in and out reads as a stall every cycle.
            MeteoconsMotionKind.Spin => new MeteoconsFrame(360.0 * t, 0, 1),
            MeteoconsMotionKind.Bob => new MeteoconsFrame(0, Swing(t, OverlayTokens.MeteoconsBobDip), 1),
            MeteoconsMotionKind.Pulse => new MeteoconsFrame(0, 0, 1.0 + (PulseMinFor(key) - 1.0) * Triangle(t)),
            _ => new MeteoconsFrame(0, 0, 1)
        };
    }

    /// <summary>
    /// The 0 → amplitude → 0 shape shared by the bob and the pulse.
    ///
    /// <c>sine.inOut</c> is applied to the WHOLE cycle first and the eased value then picks a
    /// point on the triangle — that is exactly what the old three-keyframe animation did with a
    /// single <c>SineEaseInOut</c> on the animation plus keyframes at 0, 0.5 and 1. Easing each
    /// half separately would look nearly identical but is not: the eased value at the midpoint is
    /// 0.5 either way, so the peak survives, while the ramps on either side of it get a second
    /// easing pass and the dip comes out visibly deeper. Since the brief is "visuals unchanged",
    /// this reproduces the old frame-for-frame rather than the merely-similar alternative.
    /// </summary>
    private static double Swing(double t, double amplitude) => amplitude * Triangle(AnimEase.Ease("sine.inOut", t));

    /// <summary>0 at both ends of the cycle, 1 at its middle.</summary>
    private static double Triangle(double t) => t < 0.5 ? 2.0 * t : 2.0 - 2.0 * t;

    /// <summary>Elapsed fraction into 0..1, wrapped so a negative clock reads as "just before 0".</summary>
    private static double Wrap01(double t)
    {
        t %= 1.0;
        return t < 0 ? t + 1.0 : t;
    }
}
