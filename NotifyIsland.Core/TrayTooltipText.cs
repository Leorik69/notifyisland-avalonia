namespace NotifyIsland;

/// <summary>
/// The tray tooltip, as one composed string.
/// <para>
/// Pure and in Core so it is testable: the window owns the single write, but the rules for what
/// goes into it are not a window's business. The three pieces are the unread count, the clipboard
/// privacy pause, and — since the island started reading the Windows notification centre — whether
/// the platform still lets us.
/// </para>
/// </summary>
public static class TrayTooltipText
{
    /// <summary>Cap the unread count the way both tray implementations always have.</summary>
    public const int MaxUnreadShown = 99;

    /// <summary>
    /// Compose the tooltip.
    /// </summary>
    /// <param name="unread">Unread notification count.</param>
    /// <param name="pauseMinutesRemaining">
    /// Minutes left on the clipboard privacy pause; 0 when not paused.
    /// </param>
    /// <param name="listenerKnown">
    /// False while the platform has not answered yet. A pending answer must never render as
    /// "disabled" — the user would be told their notifications are off during the first second
    /// of every launch.
    /// </param>
    /// <param name="listenerDenied">
    /// True when the platform answered and the answer was a refusal, rather than some other
    /// unavailable state.
    /// </param>
    public static string For(int unread, int pauseMinutesRemaining, bool listenerKnown, bool listenerDenied)
    {
        var text = unread > 0
            ? $"NotifyIsland ({Math.Min(unread, MaxUnreadShown)})"
            : "NotifyIsland";

        if (pauseMinutesRemaining > 0)
            text += $" — пауза {pauseMinutesRemaining} мин";

        if (listenerKnown)
            text += listenerDenied
                ? " — уведомления Windows выкл"
                : " — уведомления Windows недоступны";

        return text;
    }
}
