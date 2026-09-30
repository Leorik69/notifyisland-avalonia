using System;

namespace NotifyIsland;

/// <summary>
/// What the split clipboard half (1.12.2) actually shows: the format icon key and the
/// one-line preview text. Pure, so the wording, the ellipsis and the empty-payload
/// fallback are pinned by tests instead of by whatever the <c>TextBlock</c> happens to
/// render at the current DPI.
/// <para>
/// The preview is <em>not</em> re-derived here: <see cref="ClipboardHistory.BuildPayload"/>
/// already produced the per-format text and the correct Russian plural in
/// <see cref="OverlayPayload.Title"/>. This type only normalises and clips it for a
/// 200 DIP half. Re-implementing the plural rule would be a second source of truth.
/// </para>
/// </summary>
public static class ClipboardHalfPreview
{
    /// <summary>
    /// Characters of preview text kept before the ellipsis is added, ellipsis included in
    /// the budget. The half is ClipboardHalfW wide and the <c>TextBlock</c> only 150 DIP
    /// after the 12 DIP icon, the 4 DIP spacing and the 4/8 DIP margins; at the pill's
    /// 11 DIP Segoe UI that is roughly 22 characters of Cyrillic, so a longer cut only
    /// ever produced text the <c>CharacterEllipsis</c> threw away again. It is a ceiling,
    /// not a quota: a cut that lands on a space gives that space back.
    /// </summary>
    public const int TextMaxChars = 22;

    /// <summary>
    /// Shown when there is nothing to preview — an empty or whitespace-only text payload,
    /// a file entry with no usable path. The icon alone would read as a broken pill, so the
    /// half says what happened instead. Same wording as <see cref="ClipboardHistory"/>'s
    /// <c>None</c> title, lower-cased to match the other previews.
    /// </summary>
    public const string EmptyText = "буфер обмена";

    /// <summary>
    /// Icon key per clipboard format, resolved through <see cref="IconPackService"/> like
    /// every other island key: a vendored pack that has the glyph wins, otherwise
    /// <see cref="IslandIcons"/> answers. <see cref="ClipboardItemKind.None"/> — and
    /// therefore anything unrecognised — gets the plain clipboard silhouette, which is the
    /// same key the <see cref="OverlayKind.Clipboard"/> pill uses.
    /// </summary>
    public static string IconKeyFor(ClipboardItemKind kind) => kind switch
    {
        ClipboardItemKind.Text => "clipboard",
        ClipboardItemKind.File => "file",
        ClipboardItemKind.MultiFile => "files",
        _ => "clipboard"
    };

    /// <summary>
    /// Hex colours for the format-tinted icon brush (spec §«Цветные иконки формата»):
    /// text → accent (#9CC4FF), file → neutral (#C8C8CC), multi-file → dim accent (#7AA8FF).
    /// Hex strings on purpose: the panel and the in-ball preview both need to compose brushes
    /// from Core-side decisions, and a Core-side string round-trips through Avalonia's
    /// <c>Color.Parse</c> without taking a dependency on Avalonia types.
    /// </summary>
    public const string TextIconHex = "#9CC4FF";
    public const string FileIconHex = "#C8C8CC";
    public const string MultiFileIconHex = "#7AA8FF";

    /// <summary>Format → icon tint. <see cref="ClipboardItemKind.None"/> falls back to the file tint
    /// (neutral), which is the same fallback the icon key uses.</summary>
    public static string IconTintHexFor(ClipboardItemKind kind) => kind switch
    {
        ClipboardItemKind.Text => TextIconHex,
        ClipboardItemKind.File => FileIconHex,
        ClipboardItemKind.MultiFile => MultiFileIconHex,
        _ => FileIconHex,
    };

    /// <summary>
    /// Suffix for a row whose <see cref="ClipboardEntry.RunCount"/> is &gt; 1. Returns "" for a
    /// fresh single copy so the panel and the in-ball preview never show a stray «— ×1».
    /// <c>"first item — ×N"</c> is the wording the spec calls for; the dash is U+2014 (em).
    /// </summary>
    public static string RunSuffix(int runCount)
    {
        if (runCount <= 1) return "";
        return $" — ×{runCount}";
    }

    /// <summary>
    /// One-line preview for a payload.
    /// <para>
    /// Line breaks and tabs become single spaces: a copied multi-line string would
    /// otherwise wrap inside the 30 DIP capsule and inflate the pill. Runs of whitespace
    /// collapse and the result is trimmed, so a payload of nothing but newlines lands on
    /// <see cref="EmptyText"/> rather than on an icon with no words.
    /// </para>
    /// <para>
    /// Truncation is character-based, not pixel-based, so it is testable and DPI- and
    /// font-independent; the <c>TextBlock</c> keeps <c>CharacterEllipsis</c> as the second
    /// line of defence for the <see cref="TextMaxChars"/> that still do not fit 150 DIP
    /// (a long file name, or Cyrillic in a fallback font).
    /// </para>
    /// </summary>
    public static string TextFor(ClipboardItemKind kind, string? preview)
    {
        var s = Flatten(preview);
        if (s.Length == 0) return EmptyText;

        // File / MultiFile titles come from BuildPayload as a file name / a pluralised
        // count: both are short by construction, and the TextBlock's ellipsis handles the
        // rare long name without us inventing a second rule. Only free text is cut here.
        if (kind != ClipboardItemKind.Text || s.Length <= TextMaxChars) return s;
        var head = s.AsSpan(0, TextMaxChars - 1);
        // A cut that lands on the space after a word would otherwise render "слово …" —
        // drop that one dangling space so the ellipsis sits against the last visible letter.
        if (head.Length > 0 && head[^1] == ' ') head = head[..^1];
        return string.Concat(head, "…");
    }

    /// <summary>Convenience overload for the payload as the window receives it.</summary>
    public static string TextFor(OverlayPayload p) => TextFor(p.ClipboardItemKind, p.Title);

    /// <summary>
    /// Collapse every run of whitespace into a single space and trim the ends. Covers
    /// spaces, tabs, CR, LF, vertical tab and form feed — <c>char.IsWhiteSpace</c> rather
    /// than a fixed list, so a payload carrying \u00A0 does not render as an empty half.
    /// </summary>
    private static string Flatten(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length);
        var pendingSpace = false;
        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }
            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }
            sb.Append(ch);
        }
        return sb.ToString();
    }
}
