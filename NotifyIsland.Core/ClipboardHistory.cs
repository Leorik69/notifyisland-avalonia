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
    /// Push a new clipboard entry. De-duplicates against the most recent item (identical
    /// text/path → ignored). Older entries beyond capacity are dropped from the front.
    /// </summary>
    public void Push(ClipboardEntry entry)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));
        if (entry.Kind == ClipboardItemKind.None) return;
        if (_items.Last is { Value: var last } && last.Equals(entry)) return;
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
/// Immutable snapshot of one clipboard item as it appeared at <see cref="CapturedAt"/>.
/// </summary>
public sealed class ClipboardEntry : IEquatable<ClipboardEntry>
{
    public ClipboardItemKind Kind { get; init; }
    /// <summary>Raw text for <see cref="ClipboardItemKind.Text"/>, otherwise null.</summary>
    public string? Text { get; init; }
    /// <summary>File paths for File / MultiFile, otherwise null.</summary>
    public IReadOnlyList<string>? Paths { get; init; }
    /// <summary>UTC timestamp from the source. <see cref="DateTimeOffset.MinValue"/> means "now" (caller will fill).</summary>
    public DateTimeOffset CapturedAt { get; init; }

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

    public override bool Equals(object? obj) => Equals(obj as ClipboardEntry);
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
