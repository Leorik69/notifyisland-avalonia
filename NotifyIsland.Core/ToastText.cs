namespace NotifyIsland;

/// <summary>
/// One text element of a toast, free of any Windows type. <paramref name="Kind"/> is the
/// <c>AdaptiveNotificationContentKind</c> of the element as a plain string, so the whole fold
/// stays testable on a machine that has no notification access at all.
/// </summary>
/// <param name="Kind">Content kind: <c>Text</c>, <c>Subtitle</c>, <c>Body</c>, or <c>Undefined</c>.</param>
/// <param name="Text">The element's text. May be empty — Windows hands those over too.</param>
public readonly record struct ToastTextElement(string Kind, string Text);

/// <summary>
/// Folds the text elements of one toast into the two strings the island actually shows.
/// <para>
/// The Windows listener does not hand out "a title and a body". It hands out a flat, ordered
/// list of elements, each tagged with a content kind, and the same toast is spread over several
/// bindings. A generic toast typically looks like this:
/// </para>
/// <code>
/// binding "generic"  -> Text:     "Иван"        <- the headline
///                      Subtitle:  "Slack"       <- the attribution
/// binding "generic"  -> Body:     "встреча в 10"
/// </code>
/// <para>
/// So the mapping has to be positional as well as by kind: the first headline-looking element
/// wins the headline, and everything that is left over becomes the body. Getting this wrong is
/// not cosmetic — a toast that lands with its app name as the headline and the person as the
/// body reads like a different notification entirely.
/// </para>
/// </summary>
public static class ToastText
{
    /// <summary>Kind of the primary text of a binding.</summary>
    public const string KindText = "Text";
    /// <summary>Kind of the attribution line under a headline (app name, sender).</summary>
    public const string KindSubtitle = "Subtitle";
    /// <summary>Kind of the body text of a toast.</summary>
    public const string KindBody = "Body";

    /// <summary>Longest body kept. A toast body can be arbitrarily long, and the island is a
    /// 30 DIP capsule with an ellipsised single line — trimming here keeps the allocation and the
    /// trim pass in <c>NotificationLayout</c> proportional to what can be on screen.</summary>
    public const int MaxBodyChars = 400;

    /// <summary>
    /// Fold ordered toast elements into (headline, body). Both results are trimmed and may be
    /// empty; an empty pair is the signal that a toast carries nothing worth a capsule, and
    /// <see cref="NotificationFeed"/> rejects it.
    /// </summary>
    public static (string Headline, string Body) Fold(IEnumerable<ToastTextElement>? elements)
    {
        if (elements is null) return (string.Empty, string.Empty);

        var headline = string.Empty;
        var body = new List<string>();

        foreach (var element in elements)
        {
            var text = (element.Text ?? string.Empty).Trim();
            if (text.Length == 0) continue;

            switch (element.Kind)
            {
                case KindText:
                    // The first Text element is the headline. Later ones are body material —
                    // a toast may carry several text elements in one binding.
                    if (headline.Length == 0) headline = text;
                    else if (!headline.Equals(text, StringComparison.Ordinal)) body.Add(text);
                    break;

                case KindSubtitle:
                    // The attribution is a headline only when nothing better arrived first.
                    // A toast with a Text headline plus a Subtitle keeps the Subtitle as body,
                    // which is where "Slack" or the sender name belongs on a two-line row.
                    if (headline.Length == 0) headline = text;
                    else if (!body.Contains(text, StringComparer.Ordinal)) body.Add(text);
                    break;

                case KindBody:
                    // Never promoted to the headline. The island's row is
                    // app name / headline / body, so a body-only toast reads fine as
                    // "app name" + "text", and promoting it would move text into the
                    // headline slot and shift every other line with it.
                    if (!headline.Equals(text, StringComparison.Ordinal) &&
                        !body.Contains(text, StringComparer.Ordinal))
                        body.Add(text);
                    break;

                default:
                    // Undefined / unknown kind: real toasts carry these, and dropping them
                    // silently is how a toast ends up showing only half of itself.
                    if (headline.Length == 0) headline = text;
                    else if (!body.Contains(text, StringComparer.Ordinal)) body.Add(text);
                    break;
            }
        }

        return (headline, Collapse(body));
    }

    /// <summary>
    /// Join the body parts with newlines and cut to <see cref="MaxBodyChars"/>. A toast body
    /// arrives as separate elements; on a one-line capsule they are one run of text, and a
    /// hard cut is honest about the rest existing rather than pretending the toast was short.
    /// </summary>
    private static string Collapse(List<string> parts)
    {
        if (parts.Count == 0) return string.Empty;
        var joined = string.Join("\n", parts).Replace("\r\n", "\n", StringComparison.Ordinal);
        return joined.Length <= MaxBodyChars
            ? joined
            : joined[..MaxBodyChars].TrimEnd() + "…";
    }
}
