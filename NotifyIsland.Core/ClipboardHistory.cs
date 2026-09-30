using System;
using System.Collections.Generic;
using System.Linq;

namespace NotifyIsland;

/// <summary>
/// Pure-Core clipboard history: ring buffer of recent items with payload building
/// and deterministic sanitization. No Win32 or platform APIs — the Av project feeds
/// raw captures through <see cref="BuildPayload"/>.
///
/// v1 supports <see cref="ClipboardItemKind.Text"/>, <see cref="ClipboardItemKind.File"/>,
/// and <see cref="ClipboardItemKind.MultiFile"/>. Images / rich-text get ignored
/// (caller decides whether to surface a "skipped" state).
/// </summary>
public sealed class ClipboardHistory
{
    /// <summary>
    /// Consecutive-duplicate collapse: an entry that is byte-equal to the most recent one is
    /// folded into the existing row by bumping <see cref="ClipboardEntry.RunCount"/>, not as
    /// a new row. Bumping on the existing node keeps "first item — ×N" stable: the preview is
    /// always the value of the FIRST capture of the run, never overwritten by later copies of
    /// the same text. New content of a different shape still adds a fresh row.
    /// </summary>
    public const int DefaultDupRunCap = 99;

    /// <summary>
    /// Returns <c>true</c> when <paramref name="entry"/> matches the head of the ring by value
    /// (same Kind + byte-equal body). Extracted so <see cref="Push"/> and any future
    /// capture-time guard can share the rule, and so the equality that drives the run counter
    /// is not duplicated.
    /// </summary>
    public static bool IsSameAs(ClipboardEntry? a, ClipboardEntry b)
    {
        if (a is null) return false;
        return a.Equals(b);
    }
    /// <summary>Hard cap on the ring buffer (matches AppSettings maxItems upper bound).</summary>
    public const int HardCap = 100;
    /// <summary>Default visible count when caller asks for "current" only.</summary>
    public const int DefaultMaxItems = 25;
    /// <summary>Maximum time the pill stays up after a clipboard change.</summary>
    public const int MaxPillMs = 6000;
    /// <summary>Preview size for text payload title (full text is in Body).</summary>
    public const int TitlePreviewChars = 80;
    /// <summary>Body preview size for text payload (full text retained separately).</summary>
    public const int BodyPreviewChars = 160;

    private readonly LinkedList<ClipboardEntry> _items = new();
    private readonly int _capacity;

    public ClipboardHistory(int capacity = DefaultMaxItems)
    {
        _capacity = Math.Clamp(capacity, 1, HardCap);
    }

    public int Count => _items.Count;
    public int Capacity => _capacity;

    /// <summary>Most recent entry, or null if the history is empty.</summary>
    public ClipboardEntry? Latest => _items.Last?.Value;

    /// <summary>Snapshot of the current history, oldest → newest.</summary>
    public IReadOnlyList<ClipboardEntry> Snapshot() => _items.ToList();

    /// <summary>Snapshot of the current history, newest → oldest. Defensive copy.</summary>
    public IReadOnlyList<ClipboardEntry> SnapshotNewestFirst()
    {
        var list = new List<ClipboardEntry>(_items.Count);
        for (var node = _items.Last; node is not null; node = node.Previous)
            list.Add(node.Value);
        return list;
    }

    /// <summary>
    /// Push a new clipboard entry. Consecutive duplicates are collapsed: if the new entry is
    /// byte-equal to the most recent, the existing entry's <see cref="ClipboardEntry.RunCount"/>
    /// is bumped (capped at <see cref="DefaultDupRunCap"/>) and the original capture timestamp
    /// is preserved. Different content of any kind always adds a fresh row. Older entries
    /// beyond capacity are dropped from the front.
    /// <para>
    /// The previous behaviour — silently dropping the new entry — would leave "Ctrl+C" without
    /// any visible effect on a long-running paste spree, and a counter on the row is the only
    /// way the user can see that the listener is alive.
    /// </para>
    /// </summary>
    public void Push(ClipboardEntry entry)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));
        if (entry.Kind == ClipboardItemKind.None) return;
        var lastNode = _items.Last;
        if (lastNode is not null && IsSameAs(lastNode.Value, entry))
        {
            // Preserve the very first capture of the run: the row's preview is the first thing
            // the user pasted, not the last. Pinned state, run count and timestamp all stay on
            // that first entry. A bumped node is still the same node in the LinkedList, so the
            // "most recent" pointer does not have to move.
            var nextCount = Math.Min(lastNode.Value.RunCount + 1, DefaultDupRunCap);
            lastNode.Value = lastNode.Value with { RunCount = nextCount };
            return;
        }
        _items.AddLast(entry);
        while (_items.Count > _capacity) _items.RemoveFirst();
    }

    /// <summary>Drop the most recent entry. No-op if empty.</summary>
    public void PopLatest()
    {
        if (_items.Last is not null) _items.RemoveLast();
    }

    /// <summary>Wipe all history. Used when ClipboardEnabled is toggled off.</summary>
    public void Clear() => _items.Clear();

    /// <summary>
    /// Snapshot of pinned-then-chronological rows for the panel. Pinned first (oldest pin
    /// first), then the rest newest-first. Defensive copy; the underlying list is not mutated.
    /// </summary>
    public IReadOnlyList<ClipboardEntry> SnapshotPinnedFirst()
    {
        var list = new List<ClipboardEntry>(_items.Count);
        // Pinned: keep insertion order so "first pin shows first".
        for (var node = _items.First; node is not null; node = node.Next)
            if (node.Value.IsPinned) list.Add(node.Value);
        // Chronological: newest-first.
        for (var node = _items.Last; node is not null; node = node.Previous)
            if (!node.Value.IsPinned) list.Add(node.Value);
        return list;
    }

    /// <summary>
    /// Pin the row at <paramref name="snapshotIndex"/> in the chronological-newest-first
    /// order. The most-recent row cannot be pinned (pinned-first would constantly reshuffle
    /// the freshest item back to the top, which the spec calls out explicitly).
    /// Returns <c>true</c> on success, <c>false</c> when the index is invalid or the row is
    /// already the head.
    /// </summary>
    public bool Pin(int snapshotIndex)
    {
        if (snapshotIndex <= 0) return false;       // 0 = newest; the spec forbids pinning it.
        var target = NewestAt(snapshotIndex);
        if (target is null) return false;
        if (target.IsPinned) return false;
        target = target with { IsPinned = true };
        ReplaceByIdentity(target, _items.Find(target));
        return true;
    }

    /// <summary>Unpin by chronological-newest-first index. No-op on the head or an unpinned row.</summary>
    public bool Unpin(int snapshotIndex)
    {
        var target = NewestAt(snapshotIndex);
        if (target is null || !target.IsPinned) return false;
        target = target with { IsPinned = false };
        ReplaceByIdentity(target, _items.Find(target));
        return true;
    }

    /// <summary>Toggle a row's pin. Index 0 (newest) is permanently rejected.</summary>
    public bool TogglePin(int snapshotIndex)
    {
        var entry = NewestAt(snapshotIndex);
        if (entry is null) return false;
        return entry.IsPinned ? Unpin(snapshotIndex) : Pin(snapshotIndex);
    }

    /// <summary>
    /// Resolve an entry by chronological-newest-first index. <c>0</c> is the most-recent;
    /// <c>Count-1</c> is the oldest. Returns null when the index is out of range.
    /// </summary>
    public ClipboardEntry? NewestAt(int snapshotIndex)
    {
        if (snapshotIndex < 0 || snapshotIndex >= _items.Count) return null;
        var node = _items.Last;
        for (var i = 0; i < snapshotIndex && node is not null; i++) node = node.Previous;
        return node?.Value;
    }

    private void ReplaceByIdentity(ClipboardEntry entry, LinkedListNode<ClipboardEntry>? node)
    {
        if (node is null) return;
        node.Value = entry;
    }

    /// <summary>
    /// Build a sanitized <see cref="OverlayPayload"/> suitable for <c>SetClipboard</c>
    /// dispatch. Title/body clamped for pill width; paths stripped of empty entries.
    /// Caller still passes the result through <c>OverlayMachine.Sanitize</c> as usual.
    /// </summary>
    public static OverlayPayload BuildPayload(ClipboardEntry entry, DateTimeOffset now)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));
        var payload = new OverlayPayload
        {
            ClipboardItemKind = entry.Kind,
            ClipboardPaths = entry.Paths is { Count: > 0 } ? entry.Paths.ToList() : null,
            ClipboardCapturedAt = entry.CapturedAt == default ? now : entry.CapturedAt,
        };
        switch (entry.Kind)
        {
            case ClipboardItemKind.Text:
                payload.Title = Preview(entry.Text ?? "", TitlePreviewChars);
                payload.Body = Preview(entry.Text ?? "", BodyPreviewChars);
                payload.Subtitle = "Текст";
                break;
            case ClipboardItemKind.File:
                var fileName = entry.Paths is { Count: > 0 } ? PathLeaf(entry.Paths[0]) : "";
                payload.Title = fileName.Length > TitlePreviewChars ? fileName[..(TitlePreviewChars - 1)] + "…" : fileName;
                payload.Body = entry.Paths?[0] ?? "";
                payload.Subtitle = "Файл";
                break;
            case ClipboardItemKind.MultiFile:
                var n = entry.Paths?.Count ?? 0;
                // Russian plural: 1 файл, 2-4 файла, 5-20 файлов, 21 файл, 22-24 файла, 25-30 файлов, ...
                string plural;
                var mod10 = n % 10;
                var mod100 = n % 100;
                if (mod10 == 1 && mod100 != 11) plural = "файл";
                else if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) plural = "файла";
                else plural = "файлов";
                payload.Title = $"{n} {plural}";
                payload.Body = Preview(string.Join("\n", entry.Paths ?? new List<string>()), BodyPreviewChars);
                payload.Subtitle = "Файлы";
                break;
            case ClipboardItemKind.None:
            default:
                payload.Title = "Буфер обмена";
                break;
        }
        return payload;
    }

    /// <summary>Convenience: build and dispatch via the same DateTimeOffset.</summary>
    public OverlayPayload BuildLatestPayload(DateTimeOffset now)
    {
        return Latest is { } e ? BuildPayload(e, now) : new OverlayPayload { ClipboardItemKind = ClipboardItemKind.None };
    }

    private static string Preview(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace("\r", "").Replace("\t", "    ");
        if (s.Length <= max) return s;
        return s[..(max - 1)] + "…";
    }

    private static string PathLeaf(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        var slash = path.LastIndexOfAny(new[] { '\\', '/' });
        return slash < 0 ? path : path[(slash + 1)..];
    }
}

/// <summary>
/// Clipboard privacy-pause helper (spec §«Не реагировать 30 мин»). Pure logic in Core so the
/// tray tooltip and the listener share one definition of "is the pause still on" and one
/// phrasing of "until when".
/// </summary>
public static class ClipboardPrivacyPause
{
    /// <summary>Default duration of the pause when the user picks "Не реагировать 30 мин".</summary>
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromMinutes(30);

    /// <summary>True while <paramref name="untilUtc"/> is still in the future at <paramref name="now"/>.</summary>
    public static bool IsActive(DateTime? untilUtc, DateTime now) =>
        untilUtc is { } until && until > now;

    /// <summary>Returns the remaining minutes of the pause (rounded up to at least 1 if active).
    /// 0 when the pause has expired or was never set. Drives the tray tooltip phrasing.</summary>
    public static int RemainingMinutes(DateTime? untilUtc, DateTime now)
    {
        if (!IsActive(untilUtc, now)) return 0;
        var remaining = untilUtc!.Value - now;
        if (remaining < TimeSpan.FromMinutes(1))
            return 1;
        return (int)Math.Ceiling(remaining.TotalMinutes);
    }

    /// <summary>Activate the pause "now + duration" in UTC. Used by the "30 мин" menu item.</summary>
    public static DateTime Activate(DateTime now) => (now + DefaultDuration).ToUniversalTime();

    /// <summary>Localised tooltip text for the tray icon when the pause is active.</summary>
    public static string TooltipText(int remainingMinutes) =>
        remainingMinutes <= 0
            ? "NotifyIsland"
            : $"NotifyIsland — пауза {remainingMinutes} мин";
}

/// <summary>
/// Ball-preview cycle helper (spec §«Колесо на шарике»). Pure Core so the wrap-around and
/// "reset to newest on capture" rules are testable without an Avalonia Window.
///
/// The state is intentionally tiny: one int, the index INTO
/// <see cref="ClipboardHistory.SnapshotNewestFirst"/>. Index 0 = newest = the head of the ring.
/// Index N-1 = oldest. Wheel-down walks the user AWAY from the head (deeper into history);
/// wheel-up walks them BACK toward the newest.
/// </summary>
public static class BallPreviewCycle
{
    /// <summary>Move the cycle index by <paramref name="delta"/> with wrap-around at BOTH ends.
    /// Returns the new index.
    /// <list type="bullet">
    ///   <item>Positive delta (wheel-down) walks AWAY from newest (older → larger index).</item>
    ///   <item>Negative delta (wheel-up) walks BACK toward newest (newer → smaller index).</item>
    /// </list>
    /// The wrap is the spec's "wrap around at both ends": from the newest (0), wheel-down
    /// jumps to the oldest (N-1); from the oldest (N-1), wheel-up jumps to the newest (0). A
    /// flat index offset ("current + delta") doesn't give that behaviour, so the function
    /// negates delta before the modulo: at the OLDEST, going "back one" (delta=+1) wraps to
    /// the head.
    /// </summary>
    public static int Step(int current, int delta, int historyCount)
    {
        if (historyCount <= 0) return 0;
        var n = historyCount;
        return ((current - delta) % n + n) % n;
    }

    /// <summary>Pick the entry at <paramref name="cycleIndex"/> in newest-first order.</summary>
    public static ClipboardEntry? Resolve(
        IReadOnlyList<ClipboardEntry> newestFirst, int cycleIndex)
    {
        if (newestFirst.Count == 0) return null;
        var idx = ((cycleIndex % newestFirst.Count) + newestFirst.Count) % newestFirst.Count;
        return newestFirst[idx];
    }
}

/// <summary>
/// Immutable snapshot of one clipboard item as it appeared at <see cref="ClipboardEntry.CapturedAt"/>.
/// </summary>
public sealed record ClipboardEntry : IEquatable<ClipboardEntry>
{
    public ClipboardItemKind Kind { get; init; }
    /// <summary>Raw text for <see cref="ClipboardItemKind.Text"/>, otherwise null.</summary>
    public string? Text { get; init; }
    /// <summary>File paths for File / MultiFile, otherwise null.</summary>
    public IReadOnlyList<string>? Paths { get; init; }
    /// <summary>UTC timestamp from the source. <see cref="DateTimeOffset.MinValue"/> means "now" (caller will fill).</summary>
    public DateTimeOffset CapturedAt { get; init; }
    /// <summary>
    /// Consecutive-duplicate collapse counter. <c>1</c> for the first capture, <c>N+1</c> for
    /// each subsequent byte-equal capture. Drives the «— ×N» suffix in the panel and the in-ball
    /// preview. Equality that bumps it (<see cref="ClipboardHistory.Push"/>) ignores
    /// <c>RunCount</c>: the counter is metadata about the run, not part of the value.
    /// </summary>
    public int RunCount { get; init; } = 1;
    /// <summary>True when the user pinned the row from the panel context menu. Drives the
    /// "pinned-first" sort in <see cref="ClipboardHistory.SnapshotPinnedFirst"/>.</summary>
    public bool IsPinned { get; init; }

    public static ClipboardEntry FromText(string text, DateTimeOffset at) => new()
    {
        Kind = ClipboardItemKind.Text,
        Text = text ?? "",
        CapturedAt = at
    };

    public static ClipboardEntry FromFile(string path, DateTimeOffset at) => new()
    {
        Kind = ClipboardItemKind.File,
        Paths = new List<string> { path ?? "" },
        CapturedAt = at
    };

    public static ClipboardEntry FromFiles(IReadOnlyList<string> paths, DateTimeOffset at) => new()
    {
        Kind = ClipboardItemKind.MultiFile,
        Paths = paths ?? new List<string>(),
        CapturedAt = at
    };

    /// <summary>
    /// Records auto-generate equality from every property. We override with a content-only
    /// comparison because the dedupe in <see cref="ClipboardHistory.Push"/> must treat two
    /// captures with the same Kind/Text/Paths as equal regardless of their <see cref="RunCount"/>
    /// or <see cref="IsPinned"/> — those are metadata, not part of the value.
    /// </summary>
    public bool Equals(ClipboardEntry? other)
    {
        if (other is null) return false;
        if (Kind != other.Kind) return false;
        return Kind switch
        {
            ClipboardItemKind.Text => string.Equals(Text, other.Text, StringComparison.Ordinal),
            ClipboardItemKind.File or ClipboardItemKind.MultiFile =>
                SequencesEqual(Paths, other.Paths),
            _ => true
        };
    }

    public override int GetHashCode() => HashCode.Combine(Kind, Text ?? "", SequencesHash(Paths));

    private static bool SequencesEqual(IReadOnlyList<string>? a, IReadOnlyList<string>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
        return true;
    }

    private static int SequencesHash(IReadOnlyList<string>? seq)
    {
        if (seq is null) return 0;
        var h = new HashCode();
        foreach (var s in seq) h.Add(s ?? "");
        return h.ToHashCode();
    }
}
