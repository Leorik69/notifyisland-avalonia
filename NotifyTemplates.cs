using System;

namespace NotifyIsland;

internal static class NotifyTemplates
{
    public const string Notify = "notify";
    public const string Chat = "chat";
    public const string Mail = "mail";
    public const string Calendar = "calendar";
    public const string Call = "call";
    public const string Download = "download";
    public const string Complete = "complete";
    public const string Warn = "warn";
    public const string Focus = "focus";
    public const string System = "system";
    public const string Queue = "queue";

    public static string Normalize(string? id) => (id ?? "").Trim().ToLowerInvariant() switch
    {
        Chat => Chat,
        Mail => Mail,
        Calendar => Calendar,
        Call => Call,
        Download => Download,
        Complete => Complete,
        Warn => Warn,
        Focus => Focus,
        System => System,
        Queue or "stack" => Queue,
        "error" => "error",
        "media" => "media",
        "progress" => "progress",
        _ => Notify
    };

    public static int DurationMs(UserPrefs p, string? template)
    {
        return Normalize(template) switch
        {
            Chat => ClampMs(p.ChatDurationMs, 1800),
            Call => ClampMs(p.CallDurationMs, 6000),
            Complete => ClampMs(p.CompleteDurationMs, 2200),
            Warn => ClampMs(p.WarnDurationMs, 4000),
            _ => ClampMs(p.NotifyDurationMs, 4000)
        };
    }

    public static IslandGlyph Glyph(string? template) => Normalize(template) switch
    {
        Chat => IslandGlyph.Chat,
        Mail => IslandGlyph.Mail,
        Calendar => IslandGlyph.Calendar,
        Call => IslandGlyph.Call,
        Download => IslandGlyph.Download,
        Complete => IslandGlyph.Complete,
        Warn => IslandGlyph.Warn,
        Focus => IslandGlyph.Timer,
        System => IslandGlyph.System,
        Queue => IslandGlyph.Notify,
        _ => IslandGlyph.Notify
    };

    public static string Guess(string app, string aumid)
    {
        var s = (app + " " + aumid).ToLowerInvariant();
        if (s.Contains("telegram") || s.Contains("whatsapp") || s.Contains("discord") || s.Contains("signal") || s.Contains("slack"))
            return Chat;
        if (s.Contains("mail") || s.Contains("outlook") || s.Contains("thunderbird"))
            return Mail;
        if (s.Contains("calendar") || s.Contains("outlookcal"))
            return Calendar;
        if (s.Contains("phone") || s.Contains("call"))
            return Call;
        return Notify;
    }

    private static int ClampMs(int v, int fallback)
    {
        if (v < 500 || v > 30000) return fallback;
        return v;
    }
}
