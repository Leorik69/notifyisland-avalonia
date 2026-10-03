using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace NotifyIsland;

/// <summary>
/// The capsule morph as a declared scenario: the phases of one appear / dismiss / size change
/// plus the curve each phase runs on, so the size axis and the auxiliary axis (opacity,
/// translate, scale) cannot be desynced by a hand-edited number.
///
/// Spec: docs/superpowers/specs/2026-09-29--notifyisland-animation-layer.md ("Миграция, по одной
/// анимации за раз" — п. 2 «Морфы капсулы», and "AnimTimeline — расписание сценария"). The
/// before: <c>AppearProgress</c>/<c>DismissProgress</c> in the window picked two curves per style,
/// while the breakpoints those curves depend on (0.28 in <see cref="AnimationEasing.PopScale"/>, the
/// 0.15/0.28/0.40/0.55/0.70 plateaus in <see cref="AnimationEasing.GlitchStep"/>) were literals
/// buried inside another class. Adding a phase to a style meant remembering to edit a number
/// somewhere else. Here the whole schedule — phase names, boundaries, curves and the value each
/// phase ends on — is one list per style, and the window only reads it.
/// </summary>
/// <para>
/// Two axes, deliberately not one:
/// <list type="bullet">
/// <item>the SIZE axis is a single ease over the whole morph, named by <see cref="SizeEase"/>;</item>
/// <item>the AUX axis is composed phase by phase, each phase easing its own segment.</item>
/// </list>
/// That asymmetry is the shape of the existing picture, not an oversight: every style the app has
/// today interpolates width with one global curve, and only the auxiliary channel has real internal
/// structure (a punch that overshoots, plateaus that stutter). Splitting the size axis too would
/// re-ease an already-eased value at the boundary and visibly change the morph, which the brief
/// forbids. The one exception is the stepped Glitch, where the size axis IS a step function, so it
/// is composed from the phases like the aux one (see <see cref="Glitch"/>).
/// </para>
/// <para>
/// Pure Core, no Avalonia types: the test project cannot build a window, so anything that has to be
/// pinned by a test lives here — the same split <see cref="MeteoconsMotionTrack"/> sets.
/// </para>
/// </summary>
public sealed class CapsuleMorphTrack
{
    /// <summary>
    /// Number of equal cells the Glitch stutter alternates over. The offsets alternate per cell,
    /// so the count IS the stutter frequency; it lives here rather than in the window because the
    /// stutter and the Glitch phases are the same kind of fact about the same scenario.
    /// </summary>
    public const int GlitchStutterSteps = 12;

    /// <summary>
    /// One declared phase. <see cref="SizeStart"/>/<see cref="SizeEnd"/> and
    /// <see cref="AuxStart"/>/<see cref="AuxEnd"/> are the axis values at the phase's two ends;
    /// inside the phase the value is the blend of the two through <see cref="Ease"/>.
    /// </summary>
    public readonly record struct Step(
        string Name,
        double To,
        string Ease,
        double SizeStart,
        double SizeEnd,
        double AuxStart,
        double AuxEnd);

    /// <summary>
    /// Single-phase scenario: a soft size change with no notify styling (hover peek, monitor
    /// expand, kind change). The aux values are inert here — nothing in the window reads them for
    /// this track — but they are declared so every track has the same shape.
    /// </summary>
    public static CapsuleMorphTrack Plain { get; } =
        Build(OverlayTokens.EaseFallbackName, [new Step("morph", 1.0, OverlayTokens.EaseFallbackName, 0, 0, 0, 1)]);

    /// <summary>
    /// SlideDown / FadeScale / Inflate and Collapse / SlideUp / FadeScaleOut: one soft phase, so
    /// the two named styles differ only in the auxiliary channel the window writes.
    /// </summary>
    public static CapsuleMorphTrack SoftAppear { get; } =
        Build("power2.out", [new Step("appear", 1.0, "power2.out", 0, 0, 0, 1)]);

    /// <summary>Collapse / SlideUp / FadeScaleOut.</summary>
    public static CapsuleMorphTrack SoftDismiss { get; } =
        Build("power2.out", [new Step("dismiss", 1.0, "power2.out", 0, 0, 0, 1)]);

    /// <summary>
    /// Bounce: the damped spring, one phase. The spring's overshoot IS the effect, so there is no
    /// breakpoint to declare — a second phase would restart the spring and read as two bounces.
    /// </summary>
    public static CapsuleMorphTrack Bounce { get; } =
        Build("spring.out", [new Step("bounce", 1.0, "spring.out", 0, 0, 0, 1)]);

    /// <summary>
    /// Pop: a punch to 1.18 by 28 % of the morph, then the settle back to 1.
    /// <para>
    /// The 0.28 lives in this list and nowhere else in the scenario. <see cref="AnimationEasing.PopScale"/>
    /// also hard-codes it; both numbers are equal and the tests pin that they stay equal, so the
    /// duplicate is checkable rather than a trap.
    /// </para>
    /// </summary>
    public static CapsuleMorphTrack Pop { get; } = Build(
        "power2.out",
        [
            new Step("punch", 0.28, "power2.out", 0, 0, 0.88, 1.18),
            new Step("settle", 1.0, "power2.out", 0, 0, 1.18, 1.0),
        ]);

    /// <summary>
    /// Ragged: one phase on a LINEAR aux axis. The linear ramp is the point — a jitter that eases
    /// reads as a wobble on a spring, and the ragged leave is supposed to look like the pill lost
    /// its footing. The jitter itself is <c>Random</c> per tick and cannot be a function of t, so
    /// it stays in the window; what the scenario owns is the envelope it decays along
    /// (<c>AnimEase.Ease("ragged", t)</c>).
    /// </summary>
    public static CapsuleMorphTrack Ragged { get; } =
        Build(OverlayTokens.EaseFallbackName, [new Step("flicker", 1.0, "linear", 0, 0, 0, 1)]);

    /// <summary>
    /// Glitch: the step function, as the schedule it always was. Five plateaus and a final
    /// power2.out run to 1 — and the last phase RESTARTS from 0, not from 0.48, because the
    /// original curve has a hard cut there. That discontinuity is the stutter; smoothing it into
    /// the previous phase's end value would have made the glitch a slide.
    /// <para>
    /// Both axes are phase-composed here, so <see cref="SizeEase"/> is null: unlike every other
    /// style, the width really does jump.
    /// </para>
    /// </summary>
    public static CapsuleMorphTrack Glitch { get; } = Build(
        null,
        [
            // Each plateau declares the SAME value at both ends: a glitch HOLDS, it does not ramp.
            // The ease is linear so the composition is the step function the curve always was.
            new Step("hold", 0.15, "linear", 0.0, 0.0, 0.0, 0.0),
            new Step("jank", 0.28, "linear", 0.22, 0.22, 0.22, 0.22),
            new Step("rewind", 0.40, "linear", 0.18, 0.18, 0.18, 0.18),
            new Step("surge", 0.55, "linear", 0.55, 0.55, 0.55, 0.55),
            new Step("drop", 0.70, "linear", 0.48, 0.48, 0.48, 0.48),
            // The cut: starts at 0, not at the previous phase's 0.48.
            new Step("cut", 1.00, "power2.out", 0.0, 1.0, 0.0, 1.0),
        ]);

    private readonly AnimTimeline _timeline;
    private readonly ReadOnlyCollection<Step> _steps;
    private readonly string? _sizeEase;

    /// <summary>The declared schedule as a plain <see cref="AnimTimeline"/> — for logs and tests.</summary>
    public AnimTimeline Timeline => _timeline;

    /// <summary>All declared phases with their curves and end values, in play order.</summary>
    public IReadOnlyList<Step> Steps => _steps;

    /// <summary>
    /// The one ease the size axis runs, or null when the size axis is phase-composed like the aux
    /// one (Glitch only). See the class docs for why this axis is not split per phase.
    /// </summary>
    public string? SizeEase => _sizeEase;

    private CapsuleMorphTrack(string? sizeEase, Step[] steps)
    {
        _sizeEase = sizeEase;
        _steps = new ReadOnlyCollection<Step>(steps);
        // The Step list declares only each phase's END, so the starts are filled here by walking
        // it — which is what makes "phases tile 0..1" a consequence of the list rather than six
        // hand-copied numbers. AnimTimeline then rejects anything that does not tile, at the call
        // site that declared the scenario.
        var phases = new (string, double, double)[steps.Length];
        var from = 0.0;
        for (var i = 0; i < steps.Length; i++)
        {
            phases[i] = (steps[i].Name, from, steps[i].To);
            from = steps[i].To;
        }
        _timeline = new AnimTimeline(phases);
    }

    /// <summary>
    /// The track for a morph that plays an <em>appear</em> style. A plain morph (no notify style)
    /// is <see cref="Plain"/>.
    /// </summary>
    public static CapsuleMorphTrack ForAppear(NotifyAppearStyle style) => style switch
    {
        NotifyAppearStyle.Bounce => Bounce,
        NotifyAppearStyle.Pop => Pop,
        _ => SoftAppear
    };

    /// <summary>The track for a morph that plays a <em>dismiss</em> style.</summary>
    public static CapsuleMorphTrack ForDismiss(NotifyDismissStyle style) => style switch
    {
        NotifyDismissStyle.Ragged => Ragged,
        NotifyDismissStyle.Glitch => Glitch,
        _ => SoftDismiss
    };

    /// <summary>
    /// The size progress (0→1) at morph time <paramref name="t"/>. With a <see cref="SizeEase"/>
    /// this is that one curve over the whole morph; without it, the phase-composed value.
    /// </summary>
    public double SizeAt(double t) =>
        _sizeEase is not null ? AnimEase.Ease(_sizeEase, t) : Compose(t, static s => s.SizeStart, static s => s.SizeEnd);

    /// <summary>The auxiliary progress at morph time <paramref name="t"/> — opacity, offset, scale.</summary>
    public double AuxAt(double t) => Compose(t, static s => s.AuxStart, static s => s.AuxEnd);

    /// <summary>Which phase is playing at <paramref name="t"/>, and how far through it is.</summary>
    public (string Name, double Progress) PhaseAt(double t) => _timeline.PhaseAt(t);

    /// <summary>
    /// The stutter cell <paramref name="t"/> falls in, for the Glitch offsets. Derived from the
    /// same <see cref="GlitchStutterSteps"/> the phase list is written against, so the offsets
    /// cannot drift away from the glitch's own rhythm.
    /// </summary>
    public int StutterStep(double t) =>
        (int)(Math.Clamp(t, 0.0, 1.0) * GlitchStutterSteps) % GlitchStutterSteps;

    /// <summary>One-line description for the morph log line, so the log says which scenario ran.</summary>
    public string Describe() =>
        _sizeEase is null
            ? string.Join("+", _steps.Select(s => s.Name))
            : $"{string.Join("+", _steps.Select(s => s.Name))} size={_sizeEase}";

    /// <summary>
    /// Walk the phase list: find the phase containing <paramref name="t"/>, ease its local
    /// progress, and blend between that phase's two declared end values. Because each phase
    /// declares BOTH ends, the channel is continuous by construction — a phase cannot start
    /// somewhere its predecessor did not end unless the scenario deliberately says so (Glitch's
    /// cut), which is now a visible line in the phase list instead of a hidden literal.
    /// </summary>
    private double Compose(double t, Func<Step, double> start, Func<Step, double> end)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        var from = 0.0;
        for (var i = 0; i < _steps.Count; i++)
        {
            var step = _steps[i];
            var to = step.To;
            // Half-open [from, to) except the last phase, where t = 1 must land on it at 1.
            if (t < to || i == _steps.Count - 1)
            {
                var span = to - from;
                var local = span <= 0 ? 1.0 : Math.Clamp((t - from) / span, 0.0, 1.0);
                var a = start(step);
                var b = end(step);
                return a + (b - a) * AnimEase.Ease(step.Ease, local);
            }
            from = to;
        }
        return end(_steps[^1]);
    }

    private static CapsuleMorphTrack Build(string? sizeEase, Step[] steps)
    {
        // With a global size ease the per-phase size ends are only documentation, but they are
        // filled in with the honest value that ease has at the phase boundary rather than left at
        // 0 — otherwise the declared record would disagree with the drawn curve.
        if (sizeEase is null) return new CapsuleMorphTrack(null, steps);
        var filled = new Step[steps.Length];
        for (var i = 0; i < steps.Length; i++)
            filled[i] = steps[i] with { SizeStart = 0.0, SizeEnd = AnimEase.Ease(sizeEase, steps[i].To) };
        return new CapsuleMorphTrack(sizeEase, filled);
    }
}
