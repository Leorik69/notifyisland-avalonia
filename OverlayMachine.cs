using System;

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
    TimerComplete,
    Error,
    Stack
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
    Stack,
    Clear
}

public sealed class OverlayPayload
{
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Body { get; set; } = "";
    public double Progress { get; set; }
    public bool Playing { get; set; }
    public double RemainingSeconds { get; set; }
    public string Line2 { get; set; } = "";
    public string Template { get; set; } = "notify";
    public string AppId { get; set; } = "";
    public int DurationMs { get; set; }
    public NotifyIsland.Core.NotifyUrgency Urgency { get; set; }
    public double EtaSeconds { get; set; }
    public bool Cancellable { get; set; }
}

public sealed class OverlaySnapshot
{
    public OverlayKind Kind { get; init; }
    public OverlayPayload Payload { get; init; } = new();
    public double Width { get; init; }
    public double Height { get; init; }
    public int NotifyMsLeft { get; init; }
}

public sealed class OverlayMachine
{
    private OverlayKind _kind = OverlayKind.Idle;
    private OverlayKind _returnTo = OverlayKind.Idle;
    private int _notifyMs;
    private readonly OverlayPayload _payload = new();
    private int _notifyDurationMs = OverlayTokens.DefaultNotifyMs;

    public int NotifyDurationMs
    {
        get => _notifyDurationMs;
        set => _notifyDurationMs = Math.Clamp(value, 500, 30000);
    }

    public OverlaySnapshot Snapshot() => new()
    {
        Kind = _kind,
        Payload = Clone(_payload),
        Width = WidthFor(_kind, _payload),
        Height = HeightFor(_kind),
        NotifyMsLeft = Math.Max(0, _notifyMs)
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
                if (string.IsNullOrWhiteSpace(_payload.Template) || _payload.Template == "notify")
                    _payload.Template = NotifyTemplates.Normalize(data.Template);
                _notifyMs = data.DurationMs > 0 ? data.DurationMs : _notifyDurationMs;
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
                _notifyMs = 0;
                break;
            case OverlayCommand.SetError:
                _kind = OverlayKind.Error;
                Apply(data);
                _payload.Urgency = NotifyIsland.Core.NotifyUrgency.Error;
                if (string.IsNullOrWhiteSpace(_payload.Body) && string.IsNullOrWhiteSpace(_payload.Title))
                    _payload.Title = "Ошибка";
                _notifyMs = 0;
                break;
            case OverlayCommand.Stack:
                _kind = OverlayKind.Stack;
                Apply(data);
                if (string.IsNullOrWhiteSpace(_payload.Title))
                    _payload.Title = "Очередь";
                if (string.IsNullOrWhiteSpace(_payload.Subtitle))
                    _payload.Subtitle = "Уведомление 1";
                if (string.IsNullOrWhiteSpace(_payload.Line2))
                    _payload.Line2 = "Уведомление 2";
                _notifyMs = 0;
                break;
            case OverlayCommand.Clear:
                _kind = OverlayKind.Idle;
                _returnTo = OverlayKind.Idle;
                _notifyMs = 0;
                Apply(new OverlayPayload());
                break;
        }
        return Snapshot();
    }

    public OverlaySnapshot Tick(int deltaMs)
    {
        var dt = Math.Max(0, deltaMs);
        if (_kind == OverlayKind.Notification)
        {
            _notifyMs -= dt;
            if (_notifyMs <= 0)
            {
                _notifyMs = 0;
                _kind = _returnTo == OverlayKind.Notification ? OverlayKind.Idle : _returnTo;
            }
        }
        if (_kind == OverlayKind.Timer)
        {
            if (_payload.RemainingSeconds > 0)
                _payload.RemainingSeconds = Math.Max(0, _payload.RemainingSeconds - dt / 1000.0);
            if (_payload.RemainingSeconds <= 0.05)
            {
                _kind = OverlayKind.TimerComplete;
                _payload.RemainingSeconds = 0;
                if (string.IsNullOrWhiteSpace(_payload.Title))
                    _payload.Title = "00:00";
                _payload.Urgency = NotifyIsland.Core.NotifyUrgency.Success;
            }
        }
        if (_kind == OverlayKind.Progress)
        {
            _payload.Progress = Math.Min(1, _payload.Progress + dt / 7000.0);
            if (_payload.EtaSeconds > 0)
                _payload.EtaSeconds = Math.Max(0, _payload.EtaSeconds - dt / 1000.0);
        }
        return Snapshot();
    }

    private void Apply(OverlayPayload data)
    {
        _payload.Title = data.Title;
        _payload.Subtitle = data.Subtitle;
        _payload.Body = data.Body;
        _payload.Progress = data.Progress;
        _payload.Playing = data.Playing;
        _payload.RemainingSeconds = data.RemainingSeconds;
        _payload.Line2 = data.Line2;
        _payload.Template = NotifyTemplates.Normalize(data.Template);
        _payload.AppId = data.AppId ?? "";
        _payload.DurationMs = data.DurationMs;
        _payload.Urgency = data.Urgency;
        _payload.EtaSeconds = data.EtaSeconds;
        _payload.Cancellable = data.Cancellable;
    }

    internal static OverlayPayload Sanitize(OverlayPayload raw)
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
        var eta = raw.EtaSeconds;
        if (double.IsNaN(eta) || double.IsInfinity(eta) || eta < 0) eta = 0;
        var l2 = (raw.Line2 ?? "").Trim();
        if (l2.Length > 80) l2 = l2[..77] + "…";
        return new OverlayPayload
        {
            Title = t, Subtitle = s, Body = b, Progress = p, Playing = raw.Playing, RemainingSeconds = rem, Line2 = l2,
            Template = NotifyTemplates.Normalize(raw.Template), AppId = (raw.AppId ?? "").Trim(),
            DurationMs = raw.DurationMs, Urgency = raw.Urgency, EtaSeconds = eta, Cancellable = raw.Cancellable
        };
    }

    internal static OverlayPayload Clone(OverlayPayload p) => new()
    {
        Title = p.Title, Subtitle = p.Subtitle, Body = p.Body,
        Progress = p.Progress, Playing = p.Playing, RemainingSeconds = p.RemainingSeconds, Line2 = p.Line2,
        Template = p.Template, AppId = p.AppId, DurationMs = p.DurationMs,
        Urgency = p.Urgency, EtaSeconds = p.EtaSeconds, Cancellable = p.Cancellable
    };

    internal static double WidthFor(OverlayKind kind, OverlayPayload p)
    {
        var n = (p.Title?.Length ?? 0) + (p.Subtitle?.Length ?? 0) + (p.Body?.Length ?? 0) / 2;
        var grown = Math.Clamp(240 + n * 3.6, 240, 560);
        return kind switch
        {
            OverlayKind.Expanded => Math.Max(400, grown * 0.85),
            OverlayKind.Notification or OverlayKind.Error or OverlayKind.TimerComplete => grown,
            OverlayKind.Stack => Math.Max(400, grown),
            OverlayKind.Progress => Math.Max(380, grown),
            OverlayKind.Media => 420,
            OverlayKind.Timer => 340,
            _ => OverlayTokens.CollapsedW
        };
    }

    internal static double HeightFor(OverlayKind kind) => kind switch
    {
        OverlayKind.Idle or OverlayKind.Collapsed => OverlayTokens.CollapsedH,
        OverlayKind.Media => 96,
        OverlayKind.Stack => 108,
        OverlayKind.Expanded => 88,
        OverlayKind.Error => 80,
        OverlayKind.TimerComplete => 78,
        _ => 78
    };
}
