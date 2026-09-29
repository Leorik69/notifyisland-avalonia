using System;
using System.Collections.Generic;

namespace NotifyIsland;

/// <summary>
/// Named easing curves, GSAP-style: <c>AnimEase.Ease("power3.inOut", t)</c>.
///
/// Spec: docs/superpowers/specs/2026-09-29--notifyisland-animation-layer.md ("AnimEase — словарь кривых").
/// Idea ported from GSAP, not the library: GSAP is JS/DOM and there is no JS engine in this process,
/// so only the *vocabulary* (one dictionary of names instead of a <c>CubicOut</c> / <c>ClickPop</c> /
/// <c>Ragged</c> scatter) moves. Nothing here depends on Avalonia — Core stays pure and testable.
///
/// Power numbering follows GSAP semantics: the higher the number, the sharper the curve.
/// power1 = quad, power2 = cubic, power3 = quart, power4 = quint. (GSAP's power1..4 are the
/// polynomial degrees 2..5; the number is an index, not the degree.)
///
/// `ragged` is the one non-ease entry, added with the 1.12.4 morph migration: it is the decay
/// ENVELOPE of the ragged dismiss (1 → 0), not the jitter. The jitter is a per-tick
/// <c>Random</c> translate and cannot be a function of t, so it stays in the window — see
/// <see cref="CapsuleMorphTrack.Ragged"/>, which declares the ragged scenario as one phase on a
/// linear axis. Making the envelope a named curve is what lets the scenario declare it instead of
/// writing <c>1 - t</c> inline next to a random number.
/// </summary>
public static class AnimEase
{
    /// <summary>Degree used by <c>powerN</c>: power1 → quad … power4 → quint.</summary>
    private static readonly double[] PowerDegrees = { 2.0, 3.0, 4.0, 5.0 };

    /// <summary>
    /// Overshoot constant of <c>back.out</c>. The value GSAP uses; hard-coded rather than parsed
    /// out of "back.out(1.7)" because the name has to stay a plain dictionary key.
    /// </summary>
    private const double BackOvershoot = 1.70158;

    private static readonly Dictionary<string, Func<double, double>> Curves =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["linear"] = t => t,

            ["power1.in"] = t => Math.Pow(t, 2),
            ["power1.out"] = t => 1 - Math.Pow(1 - t, 2),
            ["power1.inOut"] = InOut(2),
            ["power2.in"] = t => Math.Pow(t, 3),
            ["power2.out"] = t => 1 - Math.Pow(1 - t, 3),
            ["power2.inOut"] = InOut(3),
            ["power3.in"] = t => Math.Pow(t, 4),
            ["power3.out"] = t => 1 - Math.Pow(1 - t, 4),
            ["power3.inOut"] = InOut(4),
            ["power4.in"] = t => Math.Pow(t, 5),
            ["power4.out"] = t => 1 - Math.Pow(1 - t, 5),
            ["power4.inOut"] = InOut(5),

            ["sine.in"] = t => 1 - Math.Cos(t * Math.PI / 2),
            ["sine.out"] = t => Math.Sin(t * Math.PI / 2),
            ["sine.inOut"] = t => -(Math.Cos(Math.PI * t) - 1) / 2,

            ["expo.in"] = t => t <= 0 ? 0 : Math.Pow(2, 10 * t - 10),
            ["expo.out"] = t => t >= 1 ? 1 : 1 - Math.Pow(2, -10 * t),
            ["expo.inOut"] = t => t <= 0 ? 0
                : t >= 1 ? 1
                : t < 0.5 ? Math.Pow(2, 20 * t - 10) / 2
                : (2 - Math.Pow(2, -20 * t + 10)) / 2,

            ["circ.in"] = t => 1 - Math.Sqrt(1 - t * t),
            ["circ.out"] = t => Math.Sqrt(1 - (t - 1) * (t - 1)),
            ["circ.inOut"] = t => t < 0.5
                ? (1 - Math.Sqrt(1 - 4 * t * t)) / 2
                : (Math.Sqrt(1 - 4 * (1 - t) * (1 - t)) + 1) / 2,

            // Goes past 1 mid-way and comes back — the "перелёт с отскоком" of the spec.
            ["back.out"] = t => 1 + (BackOvershoot + 1) * Math.Pow(t - 1, 3) + BackOvershoot * Math.Pow(t - 1, 2),
            ["back.in"] = t => (BackOvershoot + 1) * t * t * t - BackOvershoot * t * t,
            ["back.inOut"] = t => t < 0.5
                ? (Math.Pow(2 * t, 2) * ((BackOvershoot + 1) * 2 * t - BackOvershoot)) / 2
                : (Math.Pow(2 * t - 2, 2) * ((BackOvershoot + 1) * (t * 2 - 2) + BackOvershoot) + 2) / 2,

            // Existing click-acknowledgement curve, reached *through* the dictionary so there is
            // exactly one implementation of it. It stays defined in AnimationEasing (that file is
            // the live one during the staged migration); inverting the reference — making
            // AnimationEasing.ClickPop a lookup into this dictionary — is a migration-step change,
            // not infrastructure, so the shape of the curve cannot drift in the meantime.
            ["clickPop"] = AnimationEasing.ClickPop,

            // The Bounce appear style's damped spring, reached through the dictionary for the same
            // reason clickPop is: one implementation, and a style that names its curve instead of
            // calling a static. Not one of GSAP's names because it is not one of GSAP's curves.
            ["spring.out"] = AnimationEasing.SpringOut,

            // The Ragged dismiss's DECAY ENVELOPE, not the jitter itself. The jitter is
            // Random-driven per tick and cannot be a function of t, so it stays in the window
            // (see CapsuleMorphTrack.Ragged); what belongs to the scenario is how fast the
            // wobble dies out, and that is a pure 1 → 0 line. Linear on purpose: an eased decay
            // would make the leave look like it was being pulled away smoothly.
            ["ragged"] = t => 1.0 - t,
        };

    /// <summary>All registered curve names, for the settings UI and for tests.</summary>
    public static IReadOnlyCollection<string> Names => Curves.Keys;

    /// <summary>Whether <paramref name="name"/> resolves to a real curve.</summary>
    public static bool Has(string? name) => name != null && Curves.ContainsKey(name);

    /// <summary>
    /// Evaluate a named curve at <paramref name="t"/> (clamped to 0..1).
    ///
    /// An unknown name deliberately does NOT throw: <c>Name</c> is a dictionary lookup, and this
    /// code runs inside a per-frame morph tick — a throw there kills the morph loop and leaves the
    /// island frozen mid-size, which is far worse than a slightly wrong curve. So it falls back to
    /// <see cref="OverlayTokens.EaseFallbackName"/> (power2.out — the same soft settle the island
    /// morph uses today) and stays visible instead of failing silently.
    /// </summary>
    public static double Ease(string? name, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return Curves.TryGetValue(name ?? string.Empty, out var curve)
            ? curve(t)
            : Curves[OverlayTokens.EaseFallbackName](t);
    }

    /// <summary>
    /// Reduced-motion gate. Returns 1.0 when <paramref name="reduced"/> is set: the element is simply
    /// *already* in its final state, with no motion at all.
    ///
    /// Why 1.0 and not the eased value at t=1, or a much faster duration: the spec's invariant is
    /// "сниженная анимация не «ускоряет», а убирает движение" — reduced motion must not show a
    /// faster animation to anyone who cannot tolerate animation. A constant 1.0 is also idempotent,
    /// so the caller may apply it on any tick without accumulating drift.
    /// </summary>
    public static double WithReducedMotion(double eased, bool reduced) => reduced ? 1.0 : eased;

    /// <summary>Polynomial in-out of the given degree, the GSAP "powerN.inOut" formula.</summary>
    private static Func<double, double> InOut(double degree) => t => t < 0.5
        ? Math.Pow(2, degree - 1) * Math.Pow(t, degree)
        : 1 - Math.Pow(-2 * t + 2, degree) / 2;
}
