using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NotifyIsland;

/// <summary>
/// View-model for one clipboard history item shown in Settings → Буфер обмена.
/// Pure POCO so Avalonia compiled bindings can resolve it from Core (no Avalonia deps).
/// </summary>
public sealed class ClipboardItemVm
{
    public ClipboardEntry? Source { get; init; }
    public string KindGlyph { get; init; } = "";
    public string KindLabelRu { get; init; } = "";
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string TimeAgoRu { get; init; } = "";

    public static ClipboardItemVm From(ClipboardEntry e) => new()
    {
        Source = e,
        KindGlyph = e.Kind switch
        {
            ClipboardItemKind.Text => "≡",
            ClipboardItemKind.File => "▣",
            ClipboardItemKind.MultiFile => "▤",
            _ => "•"
        },
        KindLabelRu = e.Kind switch
        {
            ClipboardItemKind.Text => "Текст",
            ClipboardItemKind.File => "Файл",
            ClipboardItemKind.MultiFile => "Файлы",
            _ => "?"
        },
        Title = e.Kind switch
        {
            ClipboardItemKind.Text => string.IsNullOrEmpty(e.Text)
                ? "(пусто)"
                : e.Text!.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim(),
            ClipboardItemKind.File => e.Paths is { Count: > 0 } ? Path.GetFileName(e.Paths[0]) : "(путь пустой)",
            ClipboardItemKind.MultiFile => e.Paths is { Count: > 0 }
                ? string.Join(", ", e.Paths.Take(3).Select(Path.GetFileName)) + (e.Paths.Count > 3 ? $" … (+{e.Paths.Count - 3})" : "")
                : "(пусто)",
            _ => ""
        },
        Subtitle = e.Kind switch
        {
            ClipboardItemKind.Text => $"{(e.Text?.Length ?? 0)} символов",
            ClipboardItemKind.File => e.Paths is { Count: > 0 } ? e.Paths[0] : "",
            ClipboardItemKind.MultiFile => $"{e.Paths?.Count ?? 0} файлов",
            _ => ""
        },
        TimeAgoRu = FormatTimeAgo(DateTimeOffset.Now - e.CapturedAt)
    };

    private static string FormatTimeAgo(TimeSpan d) => d switch
    {
        { TotalSeconds: < 5 } => "только что",
        { TotalSeconds: < 60 } => $"{(int)d.TotalSeconds} с назад",
        { TotalMinutes: < 60 } => $"{(int)d.TotalMinutes} мин назад",
        { TotalHours: < 24 } => $"{(int)d.TotalHours} ч назад",
        _ => $"{(int)d.TotalDays} дн назад"
    };
}
