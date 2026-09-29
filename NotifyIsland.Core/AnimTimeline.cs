using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace NotifyIsland;

/// <summary>One named slice of a scenario: <paramref name="From"/>..<paramref name="To"/> in 0..1.</summary>
public readonly record struct AnimPhase(string Name, double From, double To)
{
    /// <summary>Length of the phase on the 0..1 timeline.</summary>
    public double Span => To - From;
}

/// <summary>
/// Scenario schedule: the phases of one animation declared as a single ordered list on 0..1.
///
/// Spec: docs/superpowers/specs/2026-09-29--notifyisland-animation-layer.md ("AnimTimeline — расписание
/// сценария"). The point is structural, not cosmetic: today the copy scenario's island spread and
/// ball detach are tied together by the hand-synced <c>BlobPeekShare</c> constant, and touching one
/// silently desyncs the other. Phases that are one list *cannot* desync — that is the invariant the
/// tests below pin down.
///
/// The timeline itself is unitless. Real milliseconds stay the job of the existing
/// <see cref="AnimationTiming"/>; convert with <see cref="ProgressOf"/> / <see cref="ElapsedMsOf"/>.
/// </summary>
public sealed class AnimTimeline
{
    private readonly IReadOnlyList<AnimPhase> _phases;
    private readonly bool _reversed;

    /// <summary>
    /// Build a timeline. Throws on a malformed schedule: a gap, an overlap, a wrong order or a
    /// range outside 0..1 is a programming error, and failing here (at the call site that declares
    /// the scenario) is the only place the mistake is still cheap to fix. A silently "repaired"
    /// timeline would just move the bug into the pixels.
    /// </summary>
    public AnimTimeline(IEnumerable<(string Name, double From, double To)> phases)
    {
        ArgumentNullException.ThrowIfNull(phases);

        var list = new List<AnimPhase>();
        var cursor = 0.0;
        foreach (var (name, from, to) in phases)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Phase name must be non-empty.", nameof(phases));
            if (to <= from)
                throw new ArgumentException($"Phase '{name}' must span a positive range.", nameof(phases));
            if (from < 0.0 || to > 1.0)
                throw new ArgumentException($"Phase '{name}' must stay inside 0..1.", nameof(phases));
            // Exact equality, not a tolerance: the phases are literal constants of the scenario, so
            // "0.4 + epsilon" is a typo, not a rounding artifact.
            if (Math.Abs(from - cursor) > 1e-12)
                throw new ArgumentException(
                    $"Phase '{name}' starts at {from}, expected {cursor} — phases must tile 0..1 with no gaps and no overlaps.",
                    nameof(phases));
            list.Add(new AnimPhase(name, from, to));
            cursor = to;
        }

        if (list.Count == 0)
            throw new ArgumentException("A timeline needs at least one phase.", nameof(phases));
        if (Math.Abs(cursor - 1.0) > 1e-12)
            throw new ArgumentException("The last phase must end at 1.0.", nameof(phases));

        _phases = new ReadOnlyCollection<AnimPhase>(list);
    }

    /// <summary>Convenience ctor for the common three-phase copy scenario.</summary>
    public AnimTimeline((string Name, double From, double To) a,
                         (string Name, double From, double To) b,
                         (string Name, double From, double To) c)
        : this(new[] { a, b, c })
    {
    }

    /// <summary>All phases, in play order.</summary>
    public IReadOnlyList<AnimPhase> Phases => _phases;

    /// <summary>True when the timeline plays backwards (see <see cref="Reverse"/>).</summary>
    public bool IsReversed => _reversed;

    private AnimTimeline(IReadOnlyList<AnimPhase> phases, bool reversed)
    {
        _phases = phases;
        _reversed = reversed;
    }

    /// <summary>
    /// The same schedule played backwards. Interrupting a morph mid-flight then becomes
    /// "finish it the other way" instead of a separate settle/apply path — the spec's
    /// <c>Reverse()</c> instead of <c>SettleBlob</c>/<c>ApplyBlobRest</c>.
    ///
    /// Implemented as a flag rather than a mirrored copy of the phase list: one phase order, one
    /// <see cref="PhaseAt"/> implementation, and the reversal cannot drift from the forward run.
    /// </summary>
    public AnimTimeline Reverse() => new(_phases, !_reversed);

    /// <summary>
    /// Name of the phase active at <paramref name="t"/> and its local 0..1 progress.
    ///
    /// Invariants (all test-pinned): <paramref name="t"/> outside 0..1 is clamped; a phase boundary
    /// belongs to the *next* phase with progress 0, so no frame ever shows a 1.0 followed by a 0.0
    /// "click"; t = 1.0 is the last phase at progress 1.
    /// </summary>
    public (string Name, double Progress) PhaseAt(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        var local = _reversed ? 1.0 - t : t;

        for (var i = 0; i < _phases.Count; i++)
        {
            var p = _phases[i];
            // Half-open [From, To) except for the very end of the last phase, where t = 1 must land
            // on the last phase with progress 1 instead of falling off the end.
            var isLast = i == _phases.Count - 1;
            if (local < p.To || (isLast && local >= p.To))
            {
                var progress = p.Span <= 0 ? 1.0 : (local - p.From) / p.Span;
                return (p.Name, Math.Clamp(progress, 0.0, 1.0));
            }
        }

        return (_phases[^1].Name, 1.0);
    }

    /// <summary>
    /// Elapsed milliseconds → 0..1 timeline position. A non-positive duration means "no time at
    /// all" (reduced motion / <c>AnimationSpeed.Off</c> scales to 0) and lands on the end state.
    /// </summary>
    public static double ProgressOf(double elapsedMs, int durationMs) =>
        durationMs <= 0 ? 1.0 : Math.Clamp(elapsedMs / durationMs, 0.0, 1.0);

    /// <summary>Inverse of <see cref="ProgressOf"/> — timeline position → elapsed milliseconds.</summary>
    public static double ElapsedMsOf(double progress, int durationMs) =>
        durationMs <= 0 ? 0.0 : Math.Clamp(progress, 0.0, 1.0) * durationMs;
}
