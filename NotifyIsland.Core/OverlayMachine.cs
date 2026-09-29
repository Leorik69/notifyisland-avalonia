using System;
using System.Collections.Generic;
using System.Linq;

namespace NotifyIsland;

public enum OverlayKind
{
    Idle,
    Collapsed,
    Expanded,
    Notification,
    Progress,
    Media,
    Timer,
    Error,
    Weather,
    Battery,
    Clipboard,
    SystemStats
}

public enum OverlayCommand
{
    Collapse,
    Expand,
    Notify,
    SetProgress,
    SetMedia,
    SetTimer,
    SetError,
    Clear,
    DemoNext,
    SetWeather,
    CycleNext,
    CyclePrev,
    ExpandWidget,
    SetBattery,
    SetClipboard,
    /// <summary>Attach a clipboard half next to the current kind instead of taking over the pill (1.12.2).</summary>
    SetClipboardSplit,
    SetClipboardCycle,
    CycleClipboardNext,
    CycleClipboardPrev,
    SetSystemStats
}

/// <summary>Discriminator for the latest clipboard item the island is showing.</summary>
public enum ClipboardItemKind
{
    /// <summary>No clipboard data yet — should not normally reach SetClipboard.</summary>
    None,
    Text,
    File,
    MultiFile
}

/// <summary>Swipe L/R cycle slots (Idle included).</summary>
public enum IslandSlot
{
    Idle,
    Notification,
    Weather,
    Media
}

public sealed class OverlayPayload
{
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Body { get; set; } = "";
    public double Progress { get; set; }
    public bool Playing { get; set; }
    public double RemainingSeconds { get; set; }
    /// <summary>True = stopwatch count-up; false = countdown.</summary>
    public bool CountUp { get; set; }
    /// <summary>Optional weather fields (SetWeather / cache).</summary>
    public double? TemperatureC { get; set; }
    public int? WeatherCode { get; set; }
    public double? PrecipProb { get; set; }
    /// <summary>Optional album art bytes (JPEG/PNG) from SMTC; null keeps kind icon.</summary>
    public byte[]? ArtworkBytes { get; set; }
    /// <summary>Discriminator for SetClipboard payloads.</summary>
    public ClipboardItemKind ClipboardItemKind { get; set; } = ClipboardItemKind.None;
    /// <summary>File paths for File/MultiFile clipboard items. Empty for Text.</summary>
    public IReadOnlyList<string>? ClipboardPaths { get; set; }
    /// <summary>UTC timestamp when the clipboard item was captured. Drives sort order later.</summary>
    public DateTimeOffset ClipboardCapturedAt { get; set; } = DateTimeOffset.MinValue;
    /// <summary>Index into the cycle list for the Idle/Collapsed clipboard browser. -1 = inactive.</summary>
    public int ClipboardCycleIndex { get; set; } = -1;
    /// <summary>Total count of clipboard history items visible in the cycle browser.</summary>
    public int ClipboardCycleCount { get; set; }
    /// <summary>Preview text for the currently cycled item (first 60 chars or filename).</summary>
    public string ClipboardCyclePreview { get; set; } = "";
    /// <summary>Per-item previews for the cycle browser (newest first). Empty when not cycling.</summary>
    public IReadOnlyList<string> ClipboardCyclePreviews { get; set; } = Array.Empty<string>();
    /// <summary>Live machine metrics; null when System Stats is disabled or sampling failed.</summary>
    public SystemSnapshot? SystemStats { get; set; }
}

public sealed class OverlaySnapshot
{
    public OverlayKind Kind { get; init; }
    public OverlayPayload Payload { get; init; } = new();
    public double Width { get; init; }
    public double Height { get; init; }
    public int NotifyMsLeft { get; init; }
    public int UnreadCount { get; init; }
    /// <summary>True while a clipboard half is attached next to the normal island content (1.12.2).</summary>
    public bool IsSplitClipboard { get; init; }
    /// <summary>Milliseconds left before the split clipboard half collapses away.</summary>
    public int SplitMsLeft { get; init; }
    public bool WeatherEnabled { get; init; }
    public OverlayPayload LastWeather { get; init; } = new();
    /// <summary>Payload of the attached clipboard half. Empty when <see cref="IsSplitClipboard"/> is false.</summary>
    public OverlayPayload SplitClipboard { get; init; } = new();
}

public sealed class OverlayMachine
{
    private OverlayKind _kind = OverlayKind.Idle;
    private OverlayKind _returnTo = OverlayKind.Idle;
    private int _notifyMs;
    private readonly OverlayPayload _payload = new();
    private int _demoIndex;
    private int _notifyDurationMs = OverlayTokens.DefaultNotifyMs;
    private int _unreadCount;
    private bool _weatherEnabled = true;
    private OverlayPayload _lastWeather = WeatherCodes.MockMoscow();
    private List<string> _cyclePreviews = new();
    private int _cycleIndex = -1;
    // 1.12.2 split pill: the island keeps its own kind while a clipboard half is attached
    // beside it. The half is orthogonal to the kind, so it carries its own payload and its
    // own lifetime — see Dispatch(SetClipboardSplit) and Tick.
    private readonly OverlayPayload _splitClipboard = new();
    private bool _isSplitClipboard;
    private int _splitMs;

    // 1.13: media / timer / progress stopped taking the capsule over. They live here as
    // status-row data and as a progress-band arbitration input, both independent of _kind.
    // This is the whole point of the change: the capsule keeps the clock and only the band
    // reacts, so a song starting no longer hides the time and a timer no longer sits on
    // screen forever. See docs/superpowers/specs/2026-09-30--notifyisland-single-capsule.md.
    // Not readonly: ApplyTo fills them through a ref, and readonly fields may not be passed
    // as ref outside a constructor. They are still never reassigned, only mutated in place.
    private OverlayPayload _media = new();
    private bool _mediaActive;
    private OverlayPayload _timer = new();
    private bool _timerActive;
    private double _timerTotalSeconds;
    private int _timerDoneMs;
    private OverlayPayload _progress = new();
    private bool _progressActive;

    /// <summary>Live media session, for the monitor's Плеер row and the capsule band.</summary>
    public OverlayPayload MediaRow => Clone(_media);

    /// <summary>True while an SMTC session is being shown.</summary>
    public bool MediaActive => _mediaActive;

    /// <summary>Live timer, for the monitor's Таймер row and the capsule band.</summary>
    public OverlayPayload TimerRow => Clone(_timer);

    /// <summary>True while a countdown/stopwatch exists (running or paused, not cancelled).</summary>
    public bool TimerActive => _timerActive;

    /// <summary>
    /// How long a finished countdown keeps showing "Время вышло" in its row (ms). Long enough
    /// to be read if the panel happens to be open, short enough that a later timer start is
    /// not fighting a stale notice.
    /// </summary>
    public const int TimerDoneMs = 12000;

    /// <summary>Total length of a countdown, needed for the band's remaining fraction. 0 = stopwatch.</summary>
    public double TimerTotalSeconds => _timerTotalSeconds;

    /// <summary>
    /// Drop the timer. Separate from <see cref="OverlayCommand.Clear"/> on purpose: cancel
    /// must not collapse the capsule or wipe the payload the user was reading — the timer is
    /// a row, and cancelling a row only removes that row.
    /// </summary>
    public void ClearTimer()
    {
        _timerActive = false;
        _timerTotalSeconds = 0;
        _timer = new OverlayPayload();
    }

    /// <summary>Drop the media row (the SMTC session ended).</summary>
    public void ClearMedia()
    {
        _mediaActive = false;
        _media = new OverlayPayload();
    }

    /// <summary>Drop the generic progress job, freeing the capsule band.</summary>
    public void ClearProgress() => _progressActive = false;

    /// <summary>Live generic progress, for the capsule band (copy operations).</summary>
    public OverlayPayload ProgressRow => Clone(_progress);

    /// <summary>True while a generic progress job is running.</summary>
    public bool ProgressActive => _progressActive;

    /// <summary>
    /// The capsule's bottom band arbitration input, assembled from whatever is running right
    /// now. Built on every call rather than cached: it is read once per Paint (~60 Hz) and
    /// has no allocation beyond the record itself.
    /// </summary>
    public ProgressBandState BandState() => new()
    {
        ClipboardActive = _progressActive,
        ClipboardProgress = _progressActive ? _progress.Progress : 0,
        MediaActive = _mediaActive,
        MediaProgress = _mediaActive ? _media.Progress : 0,
        // A finished timer still occupies its row (that is where "Время вышло" is read),
        // but it is NOT running and so must not keep the band: a parked completion notice
        // holding the capsule's only progress strip would push out whatever actually started
        // in the meantime. Played out the other way, the band is a lie about progress.
        TimerActive = _timerActive && _timer.Playing,
        TimerCountUp = _timer.CountUp,
        TimerSeconds = _timer.RemainingSeconds,
        TimerTotalSeconds = _timerTotalSeconds
    };

    public int NotifyDurationMs
    {
        get => _notifyDurationMs;
        set => _notifyDurationMs = Math.Clamp(value, 500, 30000);
    }

    public int UnreadCount => _unreadCount;

    public bool WeatherEnabled
    {
        get => _weatherEnabled;
        set => _weatherEnabled = value;
    }

    /// <summary>How many metric slots the collapsed pill shows. 0 hides the row. Set by the Av layer.</summary>
    public int StatsMetricCount { get; set; }

    /// <summary>
    /// How many rows the System Stats surface shows, from the resolved preset.
    /// 0 means the default 5-row Full panel. Set by the Av layer from AppSettings.
    /// </summary>
    public int StatsRowCount { get; set; }

    public OverlayPayload LastWeather => Clone(_lastWeather);

    /// <summary>True while a clipboard half is attached next to the normal island content (1.12.2).</summary>
    public bool IsSplitClipboard => _isSplitClipboard;

    public OverlaySnapshot Snapshot() => new()
    {
        Kind = _kind,
        Payload = Clone(_payload),
        Width = WidthFor(_kind, _weatherEnabled, statsMetricCount: StatsMetricCount,
                         splitClipboard: _isSplitClipboard),
        Height = HeightFor(_kind, StatsRowCount),
        NotifyMsLeft = Math.Max(0, _notifyMs),
        UnreadCount = _unreadCount,
        WeatherEnabled = _weatherEnabled,
        LastWeather = Clone(_lastWeather),
        IsSplitClipboard = _isSplitClipboard,
        SplitMsLeft = Math.Max(0, _splitMs),
        SplitClipboard = _isSplitClipboard ? Clone(_splitClipboard) : new OverlayPayload()
    };

    public OverlaySnapshot Dispatch(OverlayCommand command, OverlayPayload? incoming = null)
    {
        var data = Sanitize(incoming ?? new OverlayPayload());
        switch (command)
        {
            case OverlayCommand.Collapse:
                _kind = OverlayKind.Collapsed;
                _notifyMs = 0;
                ClearSplit();
                break;
            case OverlayCommand.Expand:
                _kind = OverlayKind.Expanded;
                Apply(data);
                _notifyMs = 0;
                break;
            case OverlayCommand.Notify:
                // 1.13: a notification no longer takes the capsule. It fills the payload the
                // monitor and the band read, and the kind is left alone, so a toast can
                // arrive while the stats surface is open without collapsing it.
                if (string.IsNullOrWhiteSpace(_payload.Title))
                    _payload.Title = "Уведомление";
                if (string.IsNullOrWhiteSpace(data.Title))
                    data.Title = _payload.Title;
                if (string.IsNullOrWhiteSpace(data.Body))
                    data.Body = _payload.Body;
                Apply(data);
                _notifyMs = NotifyDurationMs;
                _unreadCount = Math.Min(_unreadCount + 1, 99);
                break;
            case OverlayCommand.SetProgress:
                // 1.13: the band takes it; the kind does not move.
                ApplyTo(ref _progress, data);
                _progressActive = true;
                break;
            case OverlayCommand.SetMedia:
                ApplyTo(ref _media, data);
                if (string.IsNullOrWhiteSpace(_media.Title))
                    _media.Title = "Без названия";
                if (string.IsNullOrWhiteSpace(_media.Subtitle))
                    _media.Subtitle = "Неизвестный исполнитель";
                _mediaActive = true;
                break;
            case OverlayCommand.SetTimer:
                ApplyTo(ref _timer, data);
                if (string.IsNullOrWhiteSpace(_timer.Title))
                    _timer.Title = _timer.CountUp ? "Секундомер" : "Таймер";
                // The first countdown defines the total the band's fraction is measured
                // against; a stopwatch has none and reports 0 on purpose.
                if (!_timer.CountUp && data.RemainingSeconds > 0)
                    _timerTotalSeconds = data.RemainingSeconds;
                _timerActive = true;
                break;
            case OverlayCommand.SetError:
                // 1.13: like Notify — the kind stays, the payload carries the message.
                if (string.IsNullOrWhiteSpace(data.Body) && string.IsNullOrWhiteSpace(data.Title))
                    data.Title = "Ошибка";
                Apply(data);
                break;
            case OverlayCommand.SetWeather:
                ApplyWeather(data);
                _kind = OverlayKind.Weather;
                _notifyMs = 0;
                break;
            case OverlayCommand.CycleNext:
                Cycle(+1);
                break;
            case OverlayCommand.CyclePrev:
                Cycle(-1);
                break;
            case OverlayCommand.ExpandWidget:
                ExpandCurrentWidget();
                break;
            case OverlayCommand.Clear:
                _kind = OverlayKind.Idle;
                _returnTo = OverlayKind.Idle;
                _notifyMs = 0;
                _unreadCount = 0;
                Apply(new OverlayPayload());
                ClearSplit();
                break;
            case OverlayCommand.SetBattery:
                if (_kind != OverlayKind.Battery)
                    _returnTo = _kind == OverlayKind.Collapsed ? OverlayKind.Idle : _kind;
                _kind = OverlayKind.Battery;
                Apply(data);
                if (string.IsNullOrWhiteSpace(_payload.Title))
                    _payload.Title = "Зарядка";
                if (string.IsNullOrWhiteSpace(_payload.Subtitle) && _payload.Progress > 0)
                    _payload.Subtitle = $"{(int)Math.Round(_payload.Progress * 100)}%";
                _notifyMs = NotifyDurationMs > 0 ? Math.Min(NotifyDurationMs, BatteryAlertLogic.ChargePillMs) : BatteryAlertLogic.ChargePillMs;
                // Charge pill is transient — do not bump unread.
                break;
            case OverlayCommand.SetSystemStats:
                if (data.SystemStats is null) break;
                if (_kind != OverlayKind.SystemStats)
                    _returnTo = _kind == OverlayKind.Collapsed ? OverlayKind.Idle : _kind;
                _kind = OverlayKind.SystemStats;
                Apply(data);
                break;
            case OverlayCommand.SetClipboard:
                // Defensive: ignore empty payloads rather than blanking the pill.
                if (data.ClipboardItemKind == ClipboardItemKind.None)
                    break;
                if (_kind != OverlayKind.Clipboard)
                    _returnTo = _kind == OverlayKind.Collapsed ? OverlayKind.Idle : _kind;
                _kind = OverlayKind.Clipboard;
                Apply(data);
                if (string.IsNullOrWhiteSpace(_payload.Title))
                    _payload.Title = "Буфер обмена";
                // Longer-lived than notification — copyable payload stays around.
                _notifyMs = NotifyDurationMs > 0 ? Math.Min(NotifyDurationMs, ClipboardHistory.MaxPillMs) : ClipboardHistory.MaxPillMs;
                // No unread bump — clipboard events are not system notifications.
                break;
            case OverlayCommand.SetClipboardSplit:
                // 1.12.2: the island keeps its own kind; the clipboard rides along in a
                // half attached to the long axis. No unread bump — copying is not a
                // system notification (GUIDELINES).
                if (data.ClipboardItemKind == ClipboardItemKind.None)
                    break;
                ApplySplit(data);
                _isSplitClipboard = true;
                _splitMs = NotifyDurationMs > 0
                    ? Math.Min(NotifyDurationMs, ClipboardHistory.MaxPillMs)
                    : ClipboardHistory.MaxPillMs;
                break;
            case OverlayCommand.SetClipboardCycle:
                // Install cycle previews + index. Used by the Idle/Collapsed pill to browse
                // the last N clipboard items without leaving Idle.
                if (data.ClipboardCyclePreviews is { Count: > 0 } previews)
                {
                    _cyclePreviews = previews.ToList();
                    _cycleIndex = Math.Clamp(data.ClipboardCycleIndex, 0, previews.Count - 1);
                    _payload.ClipboardCyclePreviews = previews;
                    _payload.ClipboardCycleIndex = _cycleIndex;
                    _payload.ClipboardCycleCount = _cyclePreviews.Count;
                    _payload.ClipboardCyclePreview = _cyclePreviews[_cycleIndex];
                    _payload.Title = _cyclePreviews[_cycleIndex];
                    _payload.Subtitle = $"{_cycleIndex + 1}/{_cyclePreviews.Count}";
                }
                break;
            case OverlayCommand.CycleClipboardNext:
                CycleClipboardInternal(+1);
                break;
            case OverlayCommand.CycleClipboardPrev:
                CycleClipboardInternal(-1);
                break;
            case OverlayCommand.DemoNext:
                RunDemoStep();
                break;
        }
        return Snapshot();
    }

    /// <summary>Update weather cache without forcing Weather kind (idle minimal).</summary>
    public OverlaySnapshot UpdateWeatherCache(OverlayPayload data)
    {
        var clean = Sanitize(data);
        if (clean.TemperatureC is null && clean.WeatherCode is null)
            clean = WeatherCodes.MockMoscow();
        else if (clean.TemperatureC is { } t && clean.WeatherCode is { } c)
            clean = WeatherCodes.ToPayload(t, c, clean.PrecipProb);
        _lastWeather = Clone(clean);
        if (_kind == OverlayKind.Weather)
            Apply(Clone(_lastWeather));
        return Snapshot();
    }

    public OverlaySnapshot Tick(int deltaMs)
    {
        var dt = Math.Max(0, deltaMs);
        // 1.12.1: SystemStats no longer auto-collapses on a wall-clock timer.
        // Exit is driven by pointer leave (HoverPinMachine grace) or an explicit Collapse.
        // 1.13: Notification and Battery keep their transient lifetime — they are the only
        // two kinds left that own the capsule outright. A notification no longer CHANGES
        // the kind, it only sets the payload, so _kind stays whatever the user was looking
        // at and the countdown below returns the capsule to that.
        if (_kind == OverlayKind.Battery)
        {
            _notifyMs -= dt;
            if (_notifyMs <= 0)
            {
                _notifyMs = 0;
                _kind = _returnTo == OverlayKind.Battery ? OverlayKind.Idle : _returnTo;
                if (_kind == OverlayKind.Idle)
                    _returnTo = OverlayKind.Idle;
            }
        }
        // 1.12.2: the split clipboard half has its own lifetime. It cannot ride on _notifyMs —
        // that counter is owned by the Notification/Battery/Clipboard kinds and is reset by
        // every Notify, so a notification arriving mid-split would either kill the half early
        // or be killed by it. Same 6000 ms budget (ClipboardHistory.MaxPillMs), separate counter.
        if (_isSplitClipboard)
        {
            _splitMs -= dt;
            if (_splitMs <= 0)
            {
                _splitMs = 0;
                ClearSplit();
            }
        }
        if (_timerActive && _timer.Playing)
        {
            if (_timer.CountUp)
                _timer.RemainingSeconds = Math.Min(359999, _timer.RemainingSeconds + dt / 1000.0);
            else if (_timer.RemainingSeconds > 0)
                _timer.RemainingSeconds = Math.Max(0, _timer.RemainingSeconds - dt / 1000.0);

            // A finished countdown must not vanish silently, and it must not take the capsule
            // either — the user kept battery as the only takeover. So it parks in the timer
            // row for TimerDoneMs with the completion text, and bumps the unread count, which
            // is what makes the capsule's unread dot pulse. The signal survives; the clock
            // stays on screen.
            if (IslandTimerLogic.ShouldCompleteCountdown(_timer))
            {
                _timer.RemainingSeconds = 0;
                _timer.Playing = false;
                _timer.CountUp = false;
                _timer.Title = "Таймер";
                _timer.Body = "Время вышло";
                _timerDoneMs = TimerDoneMs;
                _unreadCount = Math.Min(_unreadCount + 1, 99);
            }
        }
        else if (_timerDoneMs > 0)
        {
            // The completion notice is a guest: after its budget the row goes back to empty
            // and the band is free for whatever runs next.
            _timerDoneMs = Math.Max(0, _timerDoneMs - dt);
            if (_timerDoneMs == 0)
            {
                _timerActive = false;
                _timerTotalSeconds = 0;
                _timer = new OverlayPayload();
            }
        }
        return Snapshot();
    }

    public IslandSlot CurrentSlot() => SlotFromKind(_kind);

    public IReadOnlyList<IslandSlot> EnabledSlots()
    {
        var list = new List<IslandSlot> { IslandSlot.Idle, IslandSlot.Notification };
        if (_weatherEnabled)
            list.Add(IslandSlot.Weather);
        list.Add(IslandSlot.Media);
        return list;
    }

    private void Cycle(int direction)
    {
        var slots = EnabledSlots();
        var cur = CurrentSlot();
        var idx = slots.ToList().IndexOf(cur);
        if (idx < 0) idx = 0;
        var next = slots[(idx + direction + slots.Count * 8) % slots.Count];
        ApplySlot(next);
    }

    private void ExpandCurrentWidget()
    {
        if (_kind is OverlayKind.Idle or OverlayKind.Collapsed)
        {
            if (_weatherEnabled)
                ApplySlot(IslandSlot.Weather);
            else
                ApplySlot(IslandSlot.Notification);
            return;
        }
        // Already expanded widget — keep kind, refresh weather text if needed.
        if (_kind == OverlayKind.Weather)
            Apply(Clone(_lastWeather));
    }

    private void ApplySlot(IslandSlot slot)
    {
        switch (slot)
        {
            case IslandSlot.Idle:
                _kind = OverlayKind.Idle;
                _notifyMs = 0;
                Apply(new OverlayPayload());
                break;
            case IslandSlot.Notification:
                // Demo seed without bumping unread (swipe cycle ≠ Notify).
                _kind = OverlayKind.Notification;
                Apply(new OverlayPayload { Title = "Сообщение", Body = "Демо уведомление" });
                _notifyMs = 0;
                break;
            case IslandSlot.Weather:
                ApplyWeather(Clone(_lastWeather));
                _kind = OverlayKind.Weather;
                _notifyMs = 0;
                break;
            case IslandSlot.Media:
                _kind = OverlayKind.Media;
                Apply(new OverlayPayload
                {
                    Title = "Night Drive",
                    Subtitle = "Local Radio",
                    Progress = 0.33,
                    Playing = true
                });
                _notifyMs = 0;
                break;
        }
    }

    private static IslandSlot SlotFromKind(OverlayKind kind) => kind switch
    {
        OverlayKind.Weather => IslandSlot.Weather,
        OverlayKind.Media => IslandSlot.Media,
        OverlayKind.Notification => IslandSlot.Notification,
        _ => IslandSlot.Idle
    };

    private void CycleClipboardInternal(int delta)
    {
        if (_cyclePreviews.Count == 0) return;
        var n = _cyclePreviews.Count;
        _cycleIndex = ((_cycleIndex + delta) % n + n) % n;
        _payload.ClipboardCycleIndex = _cycleIndex;
        _payload.ClipboardCyclePreview = _cyclePreviews[_cycleIndex];
        _payload.Title = _cyclePreviews[_cycleIndex];
        _payload.Subtitle = $"{_cycleIndex + 1}/{n}";
    }

    /// <summary>Dismiss the attached clipboard half without touching the island kind.</summary>
    public OverlaySnapshot DismissSplitClipboard()
    {
        ClearSplit();
        return Snapshot();
    }

    /// <summary>
    /// Copy only the clipboard fields onto the half's own payload. The island's own payload
    /// (title/weather/notification text) is deliberately left untouched — that is the whole
    /// point of the split.
    /// </summary>
    private void ApplySplit(OverlayPayload data)
    {
        _splitClipboard.Title = string.IsNullOrWhiteSpace(data.Title) ? "Буфер обмена" : data.Title;
        _splitClipboard.Subtitle = data.Subtitle;
        _splitClipboard.Body = data.Body;
        _splitClipboard.ClipboardItemKind = data.ClipboardItemKind;
        _splitClipboard.ClipboardPaths = data.ClipboardPaths;
        _splitClipboard.ClipboardCapturedAt = data.ClipboardCapturedAt;
    }

    private void ClearSplit()
    {
        _isSplitClipboard = false;
        _splitMs = 0;
        _splitClipboard.ClipboardItemKind = ClipboardItemKind.None;
        _splitClipboard.ClipboardPaths = null;
        _splitClipboard.ClipboardCapturedAt = DateTimeOffset.MinValue;
        _splitClipboard.Title = "";
        _splitClipboard.Subtitle = "";
        _splitClipboard.Body = "";
    }

    private void ApplyWeather(OverlayPayload data)
    {
        OverlayPayload weather;
        if (data.TemperatureC is { } t && data.WeatherCode is { } c)
            weather = WeatherCodes.ToPayload(t, c, data.PrecipProb);
        else if (!string.IsNullOrWhiteSpace(data.Title) || data.TemperatureC is not null)
        {
            weather = Clone(data);
            if (weather.TemperatureC is null) weather.TemperatureC = _lastWeather.TemperatureC ?? 18;
            if (weather.WeatherCode is null) weather.WeatherCode = _lastWeather.WeatherCode ?? 0;
            if (string.IsNullOrWhiteSpace(weather.Body))
                weather.Body = WeatherCodes.FormatExpanded(
                    weather.TemperatureC ?? 18,
                    weather.WeatherCode ?? 0,
                    weather.PrecipProb);
            if (string.IsNullOrWhiteSpace(weather.Title))
                weather.Title = WeatherCodes.LabelRu(weather.WeatherCode ?? 0);
            if (string.IsNullOrWhiteSpace(weather.Subtitle))
                weather.Subtitle = WeatherCodes.FormatMinimalTemp(weather.TemperatureC ?? 18);
        }
        else
            weather = Clone(_lastWeather);

        Apply(weather);
        _lastWeather = Clone(weather);
    }

    private void RunDemoStep()
    {
        // Alternate expand ↔ collapse so width morph is obvious in demo.
        var steps = new OverlayCommand[]
        {
            OverlayCommand.Notify, OverlayCommand.Collapse,
            OverlayCommand.SetWeather, OverlayCommand.Collapse,
            OverlayCommand.SetMedia, OverlayCommand.Collapse,
            OverlayCommand.SetProgress, OverlayCommand.Collapse,
            OverlayCommand.SetBattery, OverlayCommand.Collapse,
            OverlayCommand.SetTimer, OverlayCommand.Collapse,
            OverlayCommand.SetError, OverlayCommand.Collapse
        };
        var cmd = steps[_demoIndex % steps.Length];
        _demoIndex++;
        var sample = cmd switch
        {
            OverlayCommand.SetWeather => Clone(_lastWeather),
            OverlayCommand.Notify => new OverlayPayload { Title = "Сообщение", Body = "Демо уведомление" },
            OverlayCommand.SetProgress => new OverlayPayload { Title = "Копирование", Progress = 0.42 },
            OverlayCommand.SetMedia => new OverlayPayload { Title = "Night Drive", Subtitle = "Local Radio", Progress = 0.33, Playing = true },
            OverlayCommand.SetBattery => BatteryAlertLogic.ChargePayload(67),
            OverlayCommand.SetTimer => new OverlayPayload { Title = "Фокус", RemainingSeconds = 90, Playing = true },
            OverlayCommand.SetError => new OverlayPayload { Title = "Сеть", Body = "Нет ответа сервера" },
            _ => new OverlayPayload()
        };
        Dispatch(cmd, sample);
    }

    private void Apply(OverlayPayload data) => CopyInto(_payload, data);

    /// <summary>
    /// Copy a payload's fields into an arbitrary target. Used both for the capsule's own
    /// payload (<see cref="Apply"/>) and for the 1.13 status rows (media / timer / progress),
    /// which are the same record stored somewhere else — one copy routine, so a new payload
    /// field cannot be added to one and forgotten in the other.
    /// </summary>
    private static void CopyInto(OverlayPayload target, OverlayPayload data)
    {
        target.Title = data.Title;
        target.Subtitle = data.Subtitle;
        target.Body = data.Body;
        target.Progress = data.Progress;
        target.Playing = data.Playing;
        target.RemainingSeconds = data.RemainingSeconds;
        target.CountUp = data.CountUp;
        target.TemperatureC = data.TemperatureC;
        target.WeatherCode = data.WeatherCode;
        target.PrecipProb = data.PrecipProb;
        target.ArtworkBytes = data.ArtworkBytes;
        target.ClipboardItemKind = data.ClipboardItemKind;
        target.ClipboardPaths = data.ClipboardPaths;
        target.ClipboardCapturedAt = data.ClipboardCapturedAt;
        target.ClipboardCycleIndex = data.ClipboardCycleIndex;
        target.ClipboardCycleCount = data.ClipboardCycleCount;
        target.ClipboardCyclePreview = data.ClipboardCyclePreview;
        target.ClipboardCyclePreviews = data.ClipboardCyclePreviews;
        target.SystemStats = data.SystemStats;
    }

    /// <summary>Fill one of the 1.13 status-row payloads (media / timer / progress).</summary>
    private static void ApplyTo(ref OverlayPayload target, OverlayPayload data) => CopyInto(target, data);

    public static OverlayPayload Sanitize(OverlayPayload raw)
    {
        var t = (raw.Title ?? "").Trim();
        var s = (raw.Subtitle ?? "").Trim();
        var b = (raw.Body ?? "").Trim();
        if (t.Length > 80) t = t[..77] + "…";
        if (s.Length > 80) s = s[..77] + "…";
        if (b.Length > 160) b = b[..157] + "…";
        var p = raw.Progress;
        if (double.IsNaN(p) || double.IsInfinity(p)) p = 0;
        p = Math.Clamp(p, 0, 1);
        var rem = raw.RemainingSeconds;
        if (double.IsNaN(rem) || double.IsInfinity(rem) || rem < 0) rem = 0;
        double? temp = raw.TemperatureC;
        if (temp is { } tv && (double.IsNaN(tv) || double.IsInfinity(tv))) temp = null;
        double? precip = raw.PrecipProb;
        if (precip is { } pv)
        {
            if (double.IsNaN(pv) || double.IsInfinity(pv)) precip = null;
            else precip = Math.Clamp(pv, 0, 100);
        }
        byte[]? art = raw.ArtworkBytes;
        if (art is { Length: > 2_000_000 })
            art = null; // drop oversized artwork

        // Clipboard fields — be strict about sizes (history goes to JSON later).
        var cbKind = raw.ClipboardItemKind;
        if (!Enum.IsDefined(typeof(ClipboardItemKind), cbKind))
            cbKind = ClipboardItemKind.None;
        var cbPaths = raw.ClipboardPaths;
        if (cbPaths is { Count: > 64 })
        {
            // Cap at 64 entries; drop empty entries while we're at it.
            var trimmed = new List<string>(64);
            foreach (var entry in cbPaths)
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                if (entry.Length > 260) trimmed.Add(entry[..257] + "…");
                else trimmed.Add(entry);
                if (trimmed.Count == 64) break;
            }
            cbPaths = trimmed.Count == 0 ? null : trimmed;
        }
        else if (cbPaths is { Count: > 0 })
        {
            // No truncation needed; still null out empty entries.
            var any = false;
            foreach (var entry in cbPaths)
            {
                if (!string.IsNullOrWhiteSpace(entry)) { any = true; break; }
            }
            if (!any) cbPaths = null;
        }

        // Inferred kind from paths when caller didn't set it explicitly.
        if (cbKind == ClipboardItemKind.None && cbPaths is { Count: 1 })
            cbKind = ClipboardItemKind.File;
        else if (cbKind == ClipboardItemKind.None && cbPaths is { Count: > 1 })
            cbKind = ClipboardItemKind.MultiFile;

        return new OverlayPayload
        {
            Title = t, Subtitle = s, Body = b, Progress = p, Playing = raw.Playing,
            RemainingSeconds = rem, CountUp = raw.CountUp,
            TemperatureC = temp, WeatherCode = raw.WeatherCode, PrecipProb = precip,
            ArtworkBytes = art,
            ClipboardItemKind = cbKind,
            ClipboardPaths = cbPaths,
            ClipboardCapturedAt = raw.ClipboardCapturedAt,
            ClipboardCycleIndex = raw.ClipboardCycleIndex,
            ClipboardCycleCount = raw.ClipboardCycleCount,
            ClipboardCyclePreview = raw.ClipboardCyclePreview,
            ClipboardCyclePreviews = raw.ClipboardCyclePreviews,
            SystemStats = raw.SystemStats is { CpuPercent: var cpu } && double.IsFinite(cpu)
                ? raw.SystemStats with { CpuPercent = Math.Round(Math.Clamp(cpu, 0, 100), 1) }
                : null
        };
    }

    private static OverlayPayload Clone(OverlayPayload p) => new()
    {
        Title = p.Title, Subtitle = p.Subtitle, Body = p.Body,
        Progress = p.Progress, Playing = p.Playing, RemainingSeconds = p.RemainingSeconds,
        CountUp = p.CountUp,
        TemperatureC = p.TemperatureC, WeatherCode = p.WeatherCode, PrecipProb = p.PrecipProb,
        ArtworkBytes = p.ArtworkBytes is null ? null : (byte[])p.ArtworkBytes.Clone(),
        ClipboardItemKind = p.ClipboardItemKind,
        ClipboardPaths = p.ClipboardPaths,
        ClipboardCapturedAt = p.ClipboardCapturedAt,
        ClipboardCycleIndex = p.ClipboardCycleIndex,
        ClipboardCycleCount = p.ClipboardCycleCount,
        ClipboardCyclePreview = p.ClipboardCyclePreview,
        ClipboardCyclePreviews = p.ClipboardCyclePreviews,
        SystemStats = p.SystemStats
    };

    /// <summary>
    /// Long-axis (morph) width for a kind. <paramref name="splitClipboard"/> adds the 1.12.2
    /// clipboard half to the long axis; SystemStats keeps its fixed block width and is exempt,
    /// exactly as it is exempt from the CollapsedH height rule.
    /// </summary>
    public static double WidthFor(OverlayKind kind, bool weatherEnabled = false,
                                  bool batteryChip = false, int statsMetricCount = 0,
                                  bool splitClipboard = false)
    {
        if (kind == OverlayKind.SystemStats)
            return OverlayTokens.StatsExpandedW;
        var w = kind switch
        {
            OverlayKind.Expanded => 340,
            OverlayKind.Notification => 380,
            OverlayKind.Progress => 360,
            OverlayKind.Media => 400,
            OverlayKind.Timer => 320,
            OverlayKind.Error => 360,
            OverlayKind.Weather => 360,
            OverlayKind.Battery => 320,
            OverlayKind.Clipboard => 380,
            OverlayKind.Idle or OverlayKind.Collapsed =>
                weatherEnabled ? OverlayTokens.CollapsedWeatherW : OverlayTokens.CollapsedW,
            _ => OverlayTokens.CollapsedW
        };
        if (kind is OverlayKind.Idle or OverlayKind.Collapsed)
        {
            if (batteryChip)
                w += OverlayTokens.CollapsedBatteryExtraW;
            // The split half is orthogonal to the kind: it rides on the long axis of whatever
            // is currently shown, and is not clamped into the ExpandedMinW/MaxW range — a
            // split Notification is 380 + ClipboardHalfW, not squeezed back to 460.
            return splitClipboard ? ClipboardSplit.SplitLongAxisFor(w) : w;
        }
        w = Math.Clamp(w, OverlayTokens.ExpandedMinW, OverlayTokens.ExpandedMaxW);
        return splitClipboard ? ClipboardSplit.SplitLongAxisFor(w) : w;
    }

    /// <summary>
    /// Fixed height for every kind — island only morphs horizontally. SystemStats is the 1.12.1
    /// exception: its height follows the resolved row count (<paramref name="statsRowCount"/>,
    /// 0 or less = the default 5-row Full panel, i.e. OverlayTokens.StatsExpandedH).
    /// </summary>
    public static double HeightFor(OverlayKind kind, int statsRowCount = 0) =>
        kind == OverlayKind.SystemStats
            ? StatsLayout.StatsHeightFor(statsRowCount)
            : OverlayTokens.CollapsedH;
}
