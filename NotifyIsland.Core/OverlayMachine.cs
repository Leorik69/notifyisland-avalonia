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
    Battery
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
    SetBattery
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
}

public sealed class OverlaySnapshot
{
    public OverlayKind Kind { get; init; }
    public OverlayPayload Payload { get; init; } = new();
    public double Width { get; init; }
    public double Height { get; init; }
    public int NotifyMsLeft { get; init; }
    public int UnreadCount { get; init; }
    public bool WeatherEnabled { get; init; }
    public OverlayPayload LastWeather { get; init; } = new();
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

    public OverlayPayload LastWeather => Clone(_lastWeather);

    public OverlaySnapshot Snapshot() => new()
    {
        Kind = _kind,
        Payload = Clone(_payload),
        Width = WidthFor(_kind, _weatherEnabled),
        Height = HeightFor(_kind),
        NotifyMsLeft = Math.Max(0, _notifyMs),
        UnreadCount = _unreadCount,
        WeatherEnabled = _weatherEnabled,
        LastWeather = Clone(_lastWeather)
    };

    public OverlaySnapshot Dispatch(OverlayCommand command, OverlayPayload? incoming = null)
    {
        var data = Sanitize(incoming ?? new OverlayPayload());
        switch (command)
        {
            case OverlayCommand.Collapse:
                _kind = OverlayKind.Collapsed;
                _notifyMs = 0;
                break;
            case OverlayCommand.Expand:
                _kind = OverlayKind.Expanded;
                Apply(data);
                _notifyMs = 0;
                break;
            case OverlayCommand.Notify:
                if (_kind != OverlayKind.Notification)
                    _returnTo = _kind == OverlayKind.Collapsed ? OverlayKind.Idle : _kind;
                _kind = OverlayKind.Notification;
                Apply(data);
                if (string.IsNullOrWhiteSpace(_payload.Title))
                    _payload.Title = "Уведомление";
                _notifyMs = NotifyDurationMs;
                _unreadCount = Math.Min(_unreadCount + 1, 99);
                break;
            case OverlayCommand.SetProgress:
                _kind = OverlayKind.Progress;
                Apply(data);
                _notifyMs = 0;
                break;
            case OverlayCommand.SetMedia:
                _kind = OverlayKind.Media;
                Apply(data);
                if (string.IsNullOrWhiteSpace(_payload.Title))
                    _payload.Title = "Без названия";
                if (string.IsNullOrWhiteSpace(_payload.Subtitle))
                    _payload.Subtitle = "Неизвестный исполнитель";
                _notifyMs = 0;
                break;
            case OverlayCommand.SetTimer:
                _kind = OverlayKind.Timer;
                Apply(data);
                if (string.IsNullOrWhiteSpace(_payload.Title))
                    _payload.Title = _payload.CountUp ? "Секундомер" : "Таймер";
                _notifyMs = 0;
                break;
            case OverlayCommand.SetError:
                _kind = OverlayKind.Error;
                Apply(data);
                if (string.IsNullOrWhiteSpace(_payload.Body) && string.IsNullOrWhiteSpace(_payload.Title))
                    _payload.Title = "Ошибка";
                _notifyMs = 0;
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
        if (_kind is OverlayKind.Notification or OverlayKind.Battery)
        {
            _notifyMs -= dt;
            if (_notifyMs <= 0)
            {
                _notifyMs = 0;
                _kind = _returnTo == OverlayKind.Notification || _returnTo == OverlayKind.Battery
                    ? OverlayKind.Idle
                    : _returnTo;
            }
        }
        if (_kind == OverlayKind.Timer && _payload.Playing)
        {
            if (_payload.CountUp)
                _payload.RemainingSeconds = Math.Min(359999, _payload.RemainingSeconds + dt / 1000.0);
            else if (_payload.RemainingSeconds > 0)
                _payload.RemainingSeconds = Math.Max(0, _payload.RemainingSeconds - dt / 1000.0);

            if (IslandTimerLogic.ShouldCompleteCountdown(_kind, _payload))
            {
                _returnTo = OverlayKind.Idle;
                _kind = OverlayKind.Notification;
                Apply(IslandTimerLogic.CompletedPayload());
                _notifyMs = NotifyDurationMs;
                _unreadCount = Math.Min(_unreadCount + 1, 99);
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

    private void Apply(OverlayPayload data)
    {
        _payload.Title = data.Title;
        _payload.Subtitle = data.Subtitle;
        _payload.Body = data.Body;
        _payload.Progress = data.Progress;
        _payload.Playing = data.Playing;
        _payload.RemainingSeconds = data.RemainingSeconds;
        _payload.CountUp = data.CountUp;
        _payload.TemperatureC = data.TemperatureC;
        _payload.WeatherCode = data.WeatherCode;
        _payload.PrecipProb = data.PrecipProb;
        _payload.ArtworkBytes = data.ArtworkBytes;
    }

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
        return new OverlayPayload
        {
            Title = t, Subtitle = s, Body = b, Progress = p, Playing = raw.Playing,
            RemainingSeconds = rem, CountUp = raw.CountUp,
            TemperatureC = temp, WeatherCode = raw.WeatherCode, PrecipProb = precip,
            ArtworkBytes = art
        };
    }

    private static OverlayPayload Clone(OverlayPayload p) => new()
    {
        Title = p.Title, Subtitle = p.Subtitle, Body = p.Body,
        Progress = p.Progress, Playing = p.Playing, RemainingSeconds = p.RemainingSeconds,
        CountUp = p.CountUp,
        TemperatureC = p.TemperatureC, WeatherCode = p.WeatherCode, PrecipProb = p.PrecipProb,
        ArtworkBytes = p.ArtworkBytes is null ? null : (byte[])p.ArtworkBytes.Clone()
    };

    public static double WidthFor(OverlayKind kind, bool weatherEnabled = false, bool batteryChip = false)
    {
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
            OverlayKind.Idle or OverlayKind.Collapsed =>
                weatherEnabled ? OverlayTokens.CollapsedWeatherW : OverlayTokens.CollapsedW,
            _ => OverlayTokens.CollapsedW
        };
        if (kind is OverlayKind.Idle or OverlayKind.Collapsed)
        {
            if (batteryChip)
                w += OverlayTokens.CollapsedBatteryExtraW;
            return w;
        }
        return Math.Clamp(w, OverlayTokens.ExpandedMinW, OverlayTokens.ExpandedMaxW);
    }

    /// <summary>Fixed height for every kind — island only morphs horizontally.</summary>
    public static double HeightFor(OverlayKind kind) => OverlayTokens.CollapsedH;
}
