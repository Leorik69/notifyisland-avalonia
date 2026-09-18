using Avalonia.Media;

namespace NotifyIsland;

internal enum IslandGlyph
{
    Notify,
    Progress,
    MediaPlay,
    MediaPause,
    Timer,
    Error,
    Overview,
    Chat,
    Mail,
    Calendar,
    Call,
    Download,
    Complete,
    Warn,
    System,
    Volume,
    Mute,
    SkipNext,
    Music,
    Person,
    Image,
    Video,
    Folder,
    Link,
    Star,
    Shield,
}

internal enum WeatherGlyph
{
    Sun,
    Partly,
    Cloud,
    Rain,
    Snow,
    Thunder,
    Fog
}

internal static class IslandIcons
{
    public const string Notify = "M12 3a7 7 0 0 0-7 7v3.2L3.4 16a1 1 0 0 0 .8 1.6h15.6a1 1 0 0 0 .8-1.6L19 13.2V10a7 7 0 0 0-7-7Zm0 18a2.5 2.5 0 0 0 2.45-2h-4.9A2.5 2.5 0 0 0 12 21Z";
    public const string Progress = "M5 4h10l4 4v12a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2Zm9 1.5V9h3.5L14 5.5ZM7 12h10v1.6H7V12Zm0 4h7v1.6H7V16Z";
    public const string MediaPlay = "M8 5.8v12.4a1 1 0 0 0 1.54.84l9.2-6.2a1 1 0 0 0 0-1.68l-9.2-6.2A1 1 0 0 0 8 5.8Z";
    public const string MediaPause = "M7 5.5A1.5 1.5 0 0 1 8.5 4h1A1.5 1.5 0 0 1 11 5.5v13A1.5 1.5 0 0 1 9.5 20h-1A1.5 1.5 0 0 1 7 18.5v-13Zm6.5 0A1.5 1.5 0 0 1 15 4h1a1.5 1.5 0 0 1 1.5 1.5v13A1.5 1.5 0 0 1 16 20h-1a1.5 1.5 0 0 1-1.5-1.5v-13Z";
    public const string Timer = "M12 4a9 9 0 1 0 9 9A9 9 0 0 0 12 4Zm.8 5v4.1l3 1.8-.8 1.3L11 13.6V9h1.8ZM9 2h6v1.7H9V2Z";
    public const string Error = "M12 3.2 21 20H3L12 3.2ZM11 9h2v5h-2V9Zm0 6.5h2V18h-2v-2.5Z";
    public const string Overview = "M4 4h7v7H4V4Zm9 0h7v7h-7V4ZM4 13h7v7H4v-7Zm9 0h7v7h-7v-7Z";
    public const string MinimalDot = "M12 8.5a3.5 3.5 0 1 1 0 7 3.5 3.5 0 0 1 0-7Z";

    public const string NotifyFill = "M12 2a8 8 0 0 0-8 8v3.1L2.2 16.4A1.6 1.6 0 0 0 3.6 19h16.8a1.6 1.6 0 0 0 1.4-2.6L20 13.1V10a8 8 0 0 0-8-8Zm0 20a3.5 3.5 0 0 0 3.45-3h-6.9A3.5 3.5 0 0 0 12 22Z";
    public const string ProgressFill = "M5 3h10.2L21 8.8V19a3 3 0 0 1-3 3H5a3 3 0 0 1-3-3V6a3 3 0 0 1 3-3Zm9.2 1.8V9h4.1L14.2 4.8ZM6.5 12.2h11v2h-11v-2Zm0 4h8v2h-8v-2Z";
    public const string PlayFill = "M7.2 4.6v14.8a1.4 1.4 0 0 0 2.16 1.18l11-7.4a1.4 1.4 0 0 0 0-2.36l-11-7.4A1.4 1.4 0 0 0 7.2 4.6Z";
    public const string PauseFill = "M6.4 4.2A1.8 1.8 0 0 1 8.2 2.4h1.6A1.8 1.8 0 0 1 11.6 4.2v15.6a1.8 1.8 0 0 1-1.8 1.8H8.2a1.8 1.8 0 0 1-1.8-1.8V4.2Zm6.8 0A1.8 1.8 0 0 1 15 2.4h1.6a1.8 1.8 0 0 1 1.8 1.8v15.6a1.8 1.8 0 0 1-1.8 1.8H15a1.8 1.8 0 0 1-1.8-1.8V4.2Z";
    public const string TimerFill = "M9 1.6h6v2.2H9V1.6ZM12 4.4A8.8 8.8 0 1 0 20.8 13.2 8.8 8.8 0 0 0 12 4.4Zm1.1 4.2v4.4l3.3 2-.9 1.5-4.1-2.4V8.6h1.7Z";
    public const string ErrorFill = "M11.1 2.4 22 21.2a1.2 1.2 0 0 1-1 1.8H3a1.2 1.2 0 0 1-1-1.8L12.9 2.4a1.1 1.1 0 0 1 1.82 0ZM11 9h2v5.2h-2V9Zm0 6.8h2V18h-2v-2.2Z";
    public const string OverviewFill = "M3.4 3.4h7.6v7.6H3.4V3.4Zm9.6 0h7.6v7.6H13V3.4ZM3.4 13h7.6v7.6H3.4V13Zm9.6 0h7.6v7.6H13V13Z";

    public const string SoftNotify = "M12 4.2c-3.2 0-5.8 2.5-5.8 5.7v2.6l-1.4 2.2c-.3.5 0 1.1.6 1.1h13.2c.6 0 .9-.6.6-1.1l-1.4-2.2V9.9c0-3.2-2.6-5.7-5.8-5.7Zm-2 13.4c.3 1.1 1.1 1.8 2 1.8s1.7-.7 2-1.8H10Z";
    public const string SoftProgress = "M6.2 5.2h8.2L18.8 9v9.4c0 1-.8 1.8-1.8 1.8H6.2c-1 0-1.8-.8-1.8-1.8V7c0-1 .8-1.8 1.8-1.8Zm7.4 1.4V9h2.4l-2.4-2.4ZM8 12.2h8v1.4H8v-1.4Zm0 3.2h5.4v1.4H8V15.4Z";
    public const string SoftPlay = "M9 7.2v9.6c0 .6.7 1 1.2.7l7.2-4.8c.5-.3.5-1.1 0-1.4L10.2 6.5c-.5-.3-1.2.1-1.2.7Z";
    public const string SoftPause = "M8 7.2c0-.6.4-1 1-1h.8c.6 0 1 .4 1 1v9.6c0 .6-.4 1-1 1H9c-.6 0-1-.4-1-1V7.2Zm5.2 0c0-.6.4-1 1-1h.8c.6 0 1 .4 1 1v9.6c0 .6-.4 1-1 1h-.8c-.6 0-1-.4-1-1V7.2Z";
    public const string SoftTimer = "M12 5.2a7.2 7.2 0 1 0 7.2 7.2A7.2 7.2 0 0 0 12 5.2Zm.7 3.4v3.4l2.4 1.4-.6 1-2.9-1.8V8.6h1.1ZM9.4 3.4h5.2v1.4H9.4V3.4Z";
    public const string SoftError = "M12 5.2 19.4 18.6H4.6L12 5.2ZM11.2 9.6h1.6v4.2h-1.6V9.6Zm0 5.4h1.6v1.6h-1.6v-1.6Z";
    public const string SoftOverview = "M5.2 5.2h5.6v5.6H5.2V5.2Zm8 0h5.6v5.6H13.2V5.2ZM5.2 13.2h5.6v5.6H5.2v-5.6Zm8 0h5.6v5.6H13.2v-5.6Z";

    public const string WxSun = "M12 7.4a4.6 4.6 0 1 0 0 9.2 4.6 4.6 0 0 0 0-9.2ZM11.2 3h1.6v2.4h-1.6V3Zm0 15.6h1.6V21h-1.6v-2.4ZM3 11.2h2.4v1.6H3v-1.6Zm15.6 0H21v1.6h-2.4v-1.6ZM5.4 5.4l1.7-1.7 1.7 1.7-1.7 1.7-1.7-1.7Zm9.8 9.8 1.7-1.7 1.7 1.7-1.7 1.7-1.7-1.7ZM5.4 18.6l1.7 1.7 1.7-1.7-1.7-1.7-1.7 1.7Zm9.8-9.8 1.7 1.7 1.7-1.7-1.7-1.7-1.7 1.7Z";
    public const string WxPartly = "M9.2 8.2a3.4 3.4 0 0 1 3.2-2.4 3.4 3.4 0 0 1 3.3 2.7 4.4 4.4 0 0 1 1.7 8.3H8.4a4.2 4.2 0 0 1 .8-8.6Z";
    public const string WxCloud = "M8.2 10.2a4.2 4.2 0 0 1 4-3 4.1 4.1 0 0 1 3.9 2.8A4.6 4.6 0 0 1 17.4 19H7.4a4.2 4.2 0 0 1 .8-8.8Z";
    public const string WxRain = "M8.4 8.6a3.8 3.8 0 0 1 3.6-2.6 3.8 3.8 0 0 1 3.6 2.6A4 4 0 0 1 16.8 17H7.6A4 4 0 0 1 8.4 8.6ZM9 18.2l.8 2.2h1.2L10 18.2H9Zm3.2 0 .8 2.2h1.2l-.8-2.2h-1.2Z";
    public const string WxSnow = "M8.6 9a3.6 3.6 0 0 1 3.4-2.4A3.6 3.6 0 0 1 15.4 9 3.8 3.8 0 0 1 16.6 16.4H7.6A3.8 3.8 0 0 1 8.6 9ZM9.2 17.6l.8.8-.8.8.8.8.8-.8.8.8.8-.8-.8-.8.8-.8-.8-.8-.8.8-.8-.8-.8.8Zm4.4 0 .8.8-.8.8.8.8.8-.8.8.8.8-.8-.8-.8.8-.8-.8-.8-.8.8-.8-.8-.8.8Z";
    public const string WxThunder = "M13.6 3.6 7.8 13.2h4.2L9.6 20.4 17.8 10h-4.4l2.2-6.4h-2Z";
    public const string WxFog = "M5 9.2h14v1.6H5V9.2Zm1.4 3.2h11.2v1.6H6.4v-1.6ZM5 15.6h14V17.2H5v-1.6Z";
    public const string Chat = "M5 5h14a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H9l-4 3v-3H5a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2Zm2 4h10v1.6H7V9Zm0 3.2h7v1.6H7v-1.6Z";
    public const string Mail = "M3.5 6.2h17v11.6h-17V6.2Zm1.6 1.6 7 4.6 7-4.6v1.8l-7 4.6-7-4.6V7.8Z";
    public const string Calendar = "M6 4h2v2h8V4h2v2h2v14H4V6h2V4Zm0 6v8h12v-8H6Z";
    public const string Call = "M7.2 3.8 10 6.4 8.6 8.2a12 12 0 0 0 7.2 7.2l1.8-1.4 2.6 2.8-1.2 1.2A4.2 4.2 0 0 1 16 19.2 15.2 15.2 0 0 1 4.8 8a4.2 4.2 0 0 1 1.2-2.8L7.2 3.8Z";
    public const string Download = "M11 4h2v9.2l3-3 1.4 1.4L12 17.2 6.6 11.6 8 10.2l3 3V4ZM5 18h14v2H5v-2Z";
    public const string Complete = "M4 12a8 8 0 1 1 16 0 8 8 0 0 1-16 0Zm11.2-2.4-1.4-1.4-4.1 4.1-2-2-1.4 1.4 3.4 3.4 5.5-5.5Z";
    public const string Warn = "M12 3.4 21 20H3L12 3.4ZM11 9h2v5h-2V9Zm0 6.5h2V18h-2v-2.5Z";
    public const string Volume = "M4 9h3.2L12 5.2v13.6L7.2 15H4V9Zm10.2.4 1.4-1.4A6.2 6.2 0 0 1 17 12a6.2 6.2 0 0 1-1.4 4l-1.4-1.4A4.2 4.2 0 0 0 15.2 12a4.2 4.2 0 0 0-1-2.6Z";
    public const string Mute = "M4 9h3.2L12 5.2v5.2L6.4 16H4V9Zm12.6-3.2 1.4 1.4-3.2 3.2 3.2 3.2-1.4 1.4-3.2-3.2-3.2 3.2-1.4-1.4 3.2-3.2-3.2-3.2 1.4-1.4 3.2 3.2 3.2-3.2Z";
    public const string SkipNext = "M6 6.2 13.2 12 6 17.8V6.2ZM15 6h2v12h-2V6Z";
    public const string Music = "M9 6h10v2h-8v8.2A3.2 3.2 0 1 1 9 13.2V6Zm-1.2 9.4a1.4 1.4 0 1 0 0 2.8 1.4 1.4 0 0 0 0-2.8Z";
    public const string Person = "M12 4a3.4 3.4 0 1 1 0 6.8A3.4 3.4 0 0 1 12 4ZM6.4 18.6c.6-3 3-4.6 5.6-4.6s5 1.6 5.6 4.6H6.4Z";
    public const string Image = "M4 6h16v12H4V6Zm2 2v8h12V8H6Zm2.2 6.2 2.2-2.6 2.2 2.6 2.8-3.6 2.6 3.6H8.2Z";
    public const string Video = "M3.6 7h12v10h-12V7Zm13.2 2 4 2.4v5.2l-4 2.4V9Z";
    public const string Folder = "M3.6 6h6.2l2 2H20.4v10.4H3.6V6Z";
    public const string Link = "M9.2 12.8a3.6 3.6 0 0 1 0-5.1l2.4-2.4a3.6 3.6 0 0 1 5.1 5.1l-1.2 1.2-1.2-1.2 1.2-1.2a1.8 1.8 0 0 0-2.5-2.5L10.4 9a1.8 1.8 0 0 0 0 2.5l-1.2 1.3Zm5.6-1.6a3.6 3.6 0 0 1 0 5.1l-2.4 2.4a3.6 3.6 0 1 1-5.1-5.1l1.2-1.2 1.2 1.2-1.2 1.2a1.8 1.8 0 0 0 2.5 2.5l2.4-2.4a1.8 1.8 0 0 0 0-2.5l1.4-1.2Z";
    public const string Star = "M12 3.6 14.4 9l6 .6-4.5 4 1.3 5.8L12 16.8 6.8 19.4 8.1 13.6 3.6 9.6l6-.6L12 3.6Z";
    public const string Shield = "M12 3.2 19.2 6v6.2c0 4.2-3 7.4-7.2 8.6-4.2-1.2-7.2-4.4-7.2-8.6V6L12 3.2Z";
    public const string SysDevice = "M4 7h16v10H4V7Zm2 2v6h12V9H6Zm5 11h2v2h-2v-2Z";


    public static StreamGeometry Geometry(IslandGlyph glyph, string iconStyle)
    {
        var pack = Normalize(iconStyle);
        if (pack == "minimal") return StreamGeometry.Parse(MinimalDot);
        var data = pack switch
        {
            "fluent-fill" => Filled(glyph),
            "weather-soft" => Soft(glyph),
            "fluent-color" => Filled(glyph),
            _ => Outline(glyph)
        };
        return StreamGeometry.Parse(data);
    }

    public static StreamGeometry WeatherGeometry(WeatherGlyph glyph, string iconStyle)
    {
        var pack = Normalize(iconStyle);
        var data = glyph switch
        {
            WeatherGlyph.Sun => WxSun,
            WeatherGlyph.Partly => WxPartly,
            WeatherGlyph.Rain => WxRain,
            WeatherGlyph.Snow => WxSnow,
            WeatherGlyph.Thunder => WxThunder,
            WeatherGlyph.Fog => WxFog,
            _ => WxCloud
        };
        if (pack == "minimal") data = MinimalDot;
        return StreamGeometry.Parse(data);
    }

    public static string Mdl2(IslandGlyph glyph) => glyph switch
    {
        IslandGlyph.Notify => "\uEA8F",
        IslandGlyph.Progress => "\uE8A5",
        IslandGlyph.MediaPlay => "\uE768",
        IslandGlyph.MediaPause => "\uE769",
        IslandGlyph.Timer => "\uE916",
        IslandGlyph.Error => "\uE783",
        IslandGlyph.Chat => "\uE8BD",
        IslandGlyph.Mail => "\uE715",
        IslandGlyph.Calendar => "\uE787",
        IslandGlyph.Call => "\uE717",
        IslandGlyph.Download => "\uE896",
        IslandGlyph.Complete => "\uE73E",
        IslandGlyph.Warn => "\uE7BA",
        IslandGlyph.System => "\uE770",
        IslandGlyph.Volume => "\uE767",
        IslandGlyph.Mute => "\uE74F",
        IslandGlyph.SkipNext => "\uE893",
        IslandGlyph.Music => "\uE8D6",
        IslandGlyph.Person => "\uE77B",
        IslandGlyph.Image => "\uEB9F",
        IslandGlyph.Video => "\uE714",
        IslandGlyph.Folder => "\uE8B7",
        IslandGlyph.Link => "\uE71B",
        IslandGlyph.Star => "\uE734",
        IslandGlyph.Shield => "\uEA18",
        _ => "\uE80F"
    };

    public static string Mdl2Weather(WeatherGlyph glyph) => glyph switch
    {
        WeatherGlyph.Sun => "\uE706",
        WeatherGlyph.Partly => "\uE708",
        WeatherGlyph.Rain => "\uE753",
        WeatherGlyph.Snow => "\uE9C8",
        WeatherGlyph.Thunder => "\uE9D6",
        WeatherGlyph.Fog => "\uE9CB",
        _ => "\uE753"
    };

    public static IslandGlyph ForKind(OverlayKind kind, bool playing = false) => kind switch
    {
        OverlayKind.Notification => IslandGlyph.Notify,
        OverlayKind.Progress => IslandGlyph.Progress,
        OverlayKind.Media => playing ? IslandGlyph.MediaPause : IslandGlyph.MediaPlay,
        OverlayKind.Timer => IslandGlyph.Timer,
        OverlayKind.Error => IslandGlyph.Error,
        OverlayKind.Stack => IslandGlyph.Notify,
        _ => OverlayKind.Idle == kind ? IslandGlyph.Overview : IslandGlyph.Overview
    };

    public static IslandGlyph ForPayload(OverlayKind kind, OverlayPayload p)
    {
        var t = NotifyTemplates.Normalize(p.Template);
        if (kind == OverlayKind.Error) return IslandGlyph.Error;
        if (kind == OverlayKind.Media) return p.Playing ? IslandGlyph.MediaPause : IslandGlyph.MediaPlay;
        if (kind == OverlayKind.Progress || t == NotifyTemplates.Download) return IslandGlyph.Download;
        if (kind == OverlayKind.Timer || t == NotifyTemplates.Focus) return IslandGlyph.Timer;
        if (kind == OverlayKind.Stack || t == NotifyTemplates.Queue) return IslandGlyph.Notify;
        return NotifyTemplates.Glyph(t);
    }

    public static string Normalize(string? style) => (style ?? "").ToLowerInvariant() switch
    {
        "mdl2" => "mdl2",
        "minimal" => "minimal",
        "fluent-fill" or "filled" or "fluentfill" => "fluent-fill",
        "weather-soft" or "soft" => "weather-soft",
        "fluent-color" or "color" => "fluent-color",
        "segoe-fluent" => "mdl2",
        _ => "fluent"
    };

    private static string Outline(IslandGlyph g) => g switch
    {
        IslandGlyph.Notify => Notify,
        IslandGlyph.Progress => Progress,
        IslandGlyph.MediaPlay => MediaPlay,
        IslandGlyph.MediaPause => MediaPause,
        IslandGlyph.Timer => Timer,
        IslandGlyph.Error => Error,
        IslandGlyph.Chat => Chat,
        IslandGlyph.Mail => Mail,
        IslandGlyph.Calendar => Calendar,
        IslandGlyph.Call => Call,
        IslandGlyph.Download => Download,
        IslandGlyph.Complete => Complete,
        IslandGlyph.Warn => Warn,
        IslandGlyph.System => SysDevice,
        IslandGlyph.Volume => Volume,
        IslandGlyph.Mute => Mute,
        IslandGlyph.SkipNext => SkipNext,
        IslandGlyph.Music => Music,
        IslandGlyph.Person => Person,
        IslandGlyph.Image => Image,
        IslandGlyph.Video => Video,
        IslandGlyph.Folder => Folder,
        IslandGlyph.Link => Link,
        IslandGlyph.Star => Star,
        IslandGlyph.Shield => Shield,
        _ => Overview
    };

    private static string Filled(IslandGlyph g) => g switch
    {
        IslandGlyph.Notify => NotifyFill,
        IslandGlyph.Progress => ProgressFill,
        IslandGlyph.MediaPlay => PlayFill,
        IslandGlyph.MediaPause => PauseFill,
        IslandGlyph.Timer => TimerFill,
        IslandGlyph.Error => ErrorFill,
        IslandGlyph.Chat => Chat,
        IslandGlyph.Mail => Mail,
        IslandGlyph.Calendar => Calendar,
        IslandGlyph.Call => Call,
        IslandGlyph.Download => Download,
        IslandGlyph.Complete => Complete,
        IslandGlyph.Warn => Warn,
        IslandGlyph.System => SysDevice,
        IslandGlyph.Volume => Volume,
        IslandGlyph.Mute => Mute,
        IslandGlyph.SkipNext => SkipNext,
        IslandGlyph.Music => Music,
        IslandGlyph.Person => Person,
        IslandGlyph.Image => Image,
        IslandGlyph.Video => Video,
        IslandGlyph.Folder => Folder,
        IslandGlyph.Link => Link,
        IslandGlyph.Star => Star,
        IslandGlyph.Shield => Shield,
        _ => OverviewFill
    };

    private static string Soft(IslandGlyph g) => g switch
    {
        IslandGlyph.Notify => SoftNotify,
        IslandGlyph.Progress => SoftProgress,
        IslandGlyph.MediaPlay => SoftPlay,
        IslandGlyph.MediaPause => SoftPause,
        IslandGlyph.Timer => SoftTimer,
        IslandGlyph.Error => SoftError,
        _ => Outline(g)
    };
}
