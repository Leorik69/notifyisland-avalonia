namespace NotifyIsland;

/// <summary>
/// Presentation rules for the notification row inside the capsule.
/// <para>
/// Stage 8 asks a notification for a real hierarchy — icon, title, body, unread — but the island
/// morphs on the long axis only and every kind is exactly <see cref="OverlayTokens.CollapsedH"/>
/// tall (<c>OverlayMachine.HeightFor</c>). A stacked three-line layout would therefore need a
/// height the machine does not have, and giving Notification its own height is not on the table.
/// So the hierarchy is expressed WITHIN one line: the title keeps the primary ink and weight, the
/// body follows in the secondary ink at a smaller size, and the two are trimmed independently.
/// </para>
/// <para>
/// These are pure functions on purpose. The alternative is a width formula buried in a 4 900-line
/// code-behind, where a rounding mistake shows up as text spilling out of the pill and nothing
/// catches it; here the arithmetic is pinned by tests.
/// </para>
/// </summary>
public static class NotificationLayout
{
    /// <summary>Gap between the title and the body, in DIP. Written here so the two cannot drift.</summary>
    public const double TitleBodyGap = 6.0;

    /// <summary>Share of the available text width the title may take before it ellipsizes.</summary>
    /// <para>
    /// A notification title is the part the eye lands on and the part that names the app, so it
    /// wins the space — but it must not be allowed to push the body out entirely either, which is
    /// what a pure "title takes what it needs" rule does with a long title.
    /// </para>
    /// </summary>
    public const double TitleShare = 0.62;

    /// <summary>Narrowest title worth showing before the row is treated as body-only (DIP).</summary>
    public const double MinTitleW = 48.0;

    /// <summary>
    /// Split a payload's title and body into the two texts the row draws.
    /// <para>
    /// The body falls back to <see cref="OverlayPayload.Subtitle"/> exactly as the overlay always
    /// did — that fallback is where most notifications get their second line from, and moving it
    /// here keeps the "which text goes where" decision in one tested place instead of inline in
    /// <c>Paint</c>.
    /// </para>
    /// </summary>
    public static (string Title, string Body) Split(OverlayPayload payload, string fallbackTitle)
    {
        var title = string.IsNullOrWhiteSpace(payload.Title) ? fallbackTitle : payload.Title.Trim();
        var body = string.IsNullOrWhiteSpace(payload.Body) ? payload.Subtitle : payload.Body;
        return (Collapse(title), Collapse(body));
    }

    /// <summary>
    /// Whitespace inside a notification is never meaningful, and a stray newline in a toast body
    /// would otherwise become a second line the pill cannot show. Runs of whitespace (including
    /// newlines) collapse to one space.
    /// </summary>
    public static string Collapse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var s = value.Trim();
        var sb = new System.Text.StringBuilder(s.Length);
        var pendingSpace = false;
        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch)) { pendingSpace = sb.Length > 0; continue; }
            if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
            sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Width budgets for the two texts, given the width the row may use and whether the actions
    /// are currently on screen.
    /// <para>
    /// The title is measured from the FULL width even when the action is showing: the action is
    /// the thing that has to appear without shoving the message sideways, and the body is the part
    /// that can afford to lose characters. Both numbers are non-negative and together never
    /// exceed what is left of the row, so a long title plus a long body can only lose
    /// characters — never the badge, and never the pill.
    /// </para>
    /// </summary>
    public static (double TitleW, double BodyW) SplitWidths(double availableW, bool actionsVisible, bool hasBody)
    {
        var full = Math.Max(0, availableW);
        // With no body there is nothing to share with, so the title may use the whole row. This is
        // an upper bound, not a demand: the action lives in its own Auto column, so the text
        // column is already narrower when it is showing.
        if (!hasBody) return (full, 0);

        var title = Math.Max(MinTitleW, full * TitleShare);
        if (title > full) title = full;
        // The action sits on the trailing edge, so it takes its width out of the body budget
        // before anything else is measured. Measuring after the layout pass instead is what used
        // to let a hovered notification shove its own text a frame late.
        var budget = Math.Max(0, full - (actionsVisible ? OverlayTokens.NotifActionW : 0));
        var body = budget - title - TitleBodyGap;
        if (body < 0) body = 0;
        return (title, body);
    }
}
