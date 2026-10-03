namespace NotifyIsland;

/// <summary>
/// Resolution of the global "reduced motion" flag.
///
/// Spec: docs/superpowers/specs/2026-09-29--notifyisland-animation-layer.md ("AnimReduced — режим
/// сниженной анимации"). On Windows the OS switch is «Показывать анимации в Windows», which Avalonia
/// surfaces as <c>SystemParameters.ClientAreaAnimation</c> (SPI_GETCLIENTAREAANIMATION).
///
/// The whole function is pure: the caller reads the OS value and the user toggle and hands both in.
/// The OS read itself stays in the app layer on purpose — Core must not reference Avalonia types, and
/// <c>SystemParameters</c> cannot be faked in a test. Wiring the call site is a migration-step
/// change (the animation engine is untouched on purpose in this stage).
/// </summary>
public static class AnimReduced
{
    /// <summary>
    /// Final reduced-motion flag: on when the user asked for it OR the OS has animations off.
    ///
    /// OR, not AND, and not "OS wins": the user toggle is an independent, opt-in accessibility
    /// request that must hold even on a machine whose OS animations are on, while an OS that has
    /// animations off must never be overridden by our default <c>false</c> — that would make the OS
    /// accessibility setting a no-op for exactly the users who need it.
    ///
    /// <paramref name="userReducedMotion"/> is <see cref="AppSettings.ReducedMotion"/>, whose default
    /// is <c>false</c> = "no extra request", so an untouched install behaves exactly as before. A
    /// tri-state Auto/On/Off enum would express "always follow the OS" better, but it would change the
    /// JSON type of an existing key; that is a settings-migration decision, not infrastructure.
    /// </summary>
    public static bool Resolve(bool osAnimationsEnabled, bool userReducedMotion) =>
        !osAnimationsEnabled || userReducedMotion;
}
