using System;
using System.Collections.Generic;

namespace NotifyIsland;

/// <summary>
/// One row of the clipboard history panel (spec 1.12.3, §«Панель истории»): which icon the
/// format gets, the one-line title and how long ago it was copied.
///
/// The row is a VIEW MODEL, not a control: Core must not reference Avalonia, and the panel
/// builds its controls from these. The title and the icon are NOT re-derived here — they come
/// from <see cref="ClipboardHistory.BuildPayload"/> and <see cref="ClipboardHalfPreview"/>, which
/// already own the per-format wording and the Russian plurals. A second text rule in this file
/// would be a second source of truth that the tests could disagree with.
/// </summary>
public sealed record ClipboardHistoryRow(
    string IconKey,
    string Title,
    string AgeText,
    ClipboardEntry Entry,
    /// <summary>Consecutive-duplicate count for this row. 1 = first copy; ≥ 2 means the row was
    /// bumped because the user pasted the same value again. Drives the «— ×N» suffix.</summary>
    int RunCount,
    /// <summary>True when the user pinned the row from the panel context menu. Drives both
    /// the sort order and the row's "закреп" badge.</summary>
    bool IsPinned,
    /// <summary>This row's index in the CHRONOLOGICAL newest-first order, which is what
    /// <c>Pin</c>, <c>Unpin</c> and <c>RemoveAt</c> take.</summary>
    /// <para>
    /// It is NOT the row's own position in the panel: the panel sorts pinned rows to the top by
    /// age, so those two numbers diverge as soon as anything is pinned. Carrying the history
    /// index on the row is what makes «delete this one» mean this one.
    /// </para></summary>
    int HistoryIndex);

/// <summary>
/// Builds the panel's rows and the «сколько времени назад» wording.
/// <para>
/// The age wording is the only genuinely new text rule in the panel — the idle pill's preview
/// cycle never carried a timestamp — so it lives here, in Core, and is pinned by tests at every
/// boundary: 0 s, 59 s, 60 s, one hour, 23 h 59 min, a full day and beyond.
/// </para>
/// </summary>
public static class ClipboardHistoryRows
{
    /// <summary>Below a minute the copy is still "just now" — 59 s must not read as «1 мин».</summary>
    public const string JustNow = "только что";

    /// <summary>Exactly one hour. «час» rather than «1 ч»: the numeral reads as a measurement here.</summary>
    public const string OneHour = "час";

    /// <summary>Yesterday. Spoken for everything from a full day up to just under two.</summary>
    public const string Yesterday = "вчера";

    /// <summary>
    /// Rows for the panel, pinned-first then chronological-newest-first, capped at
    /// <paramref name="maxRows"/>. Pinned rows render in pin order (oldest pin first), so the
    /// user sees the row they care about at the very top without losing the rest of the stack
    /// below.
    ///
    /// The sort is done by <see cref="ClipboardHistory.SnapshotPinnedFirst"/>; the panel and the
    /// tray read the SAME history so the pinned-first ordering here is the one the tray's
    /// submenu reflects too. <paramref name="now"/> is passed in rather than read from the
    /// clock, so the whole row set is a pure function of (history, now) and is testable.
    /// </summary>
    public static IReadOnlyList<ClipboardHistoryRow> Build(
        ClipboardHistory? history, DateTimeOffset now, int maxRows = OverlayTokens.HistoryPanelMaxRows)
    {
        var rows = new List<ClipboardHistoryRow>();
        if (history is null || maxRows <= 0) return rows;

        // Clamp to the token: the panel's height is a token-derived constant, so a caller asking
        // for more rows than fit would overflow the window the panel is laid out inside.
        var limit = Math.Min(maxRows, OverlayTokens.HistoryPanelMaxRows);
        foreach (var (historyIndex, entry) in history.SnapshotPinnedFirstIndexed())
        {
            if (rows.Count >= limit) break;
            var payload = ClipboardHistory.BuildPayload(entry, now);
            rows.Add(new ClipboardHistoryRow(
                ClipboardHalfPreview.IconKeyFor(entry.Kind),
                ClipboardHalfPreview.TextFor(payload),
                AgeTextFor(payload.ClipboardCapturedAt, now),
                entry,
                entry.RunCount,
                entry.IsPinned,
                historyIndex));
        }
        return rows;
    }

    /// <summary>
    /// «сколько времени назад» in Russian, coarse on purpose: the panel is a list you scan, not
    /// a log you read, and a countdown that changes under the cursor is noise.
    /// <list type="bullet">
    ///   <item>&lt; 1 min → «только что»;</item>
    ///   <item>&lt; 1 h → «N мин» (1…59);</item>
    ///   <item>&lt; 24 h → «час» at exactly one hour, otherwise «N ч»;</item>
    ///   <item>&lt; 48 h → «вчера»;</item>
    ///   <item>beyond → «N дн».</item>
    /// </list>
    /// </summary>
    public static string AgeTextFor(DateTimeOffset capturedAt, DateTimeOffset now)
    {
        // A capture "from the future" (clock skew, or an entry stamped by a machine running
        // ahead) clamps to zero rather than printing a negative age.
        var age = now - capturedAt;
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;
        return AgeTextFor(age);
    }

    /// <summary>The same wording from an age that is already a span.</summary>
    public static string AgeTextFor(TimeSpan age)
    {
        if (age < TimeSpan.FromMinutes(1)) return JustNow;

        var minutes = (int)age.TotalMinutes;
        if (minutes < 60) return $"{minutes} мин";

        var hours = (int)age.TotalHours;
        if (hours < 24) return hours == 1 ? OneHour : $"{hours} ч";

        // Floor, not round: 47 h 59 min is still yesterday, and rounding would call it 2 дн.
        var days = (int)age.TotalDays;
        if (days < 2) return Yesterday;
        return $"{days} дн";
    }
}
