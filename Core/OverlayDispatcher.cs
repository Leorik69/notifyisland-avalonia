using System;

namespace NotifyIsland.Core;

public sealed class OverlayDispatcher
{
    private readonly OverlayMachine _machine;
    private readonly NotificationQueue _queue = new();

    public NotificationQueue Queue => _queue;
    public event Action? Changed;
    public event Action<NotificationEvent>? ActionInvoked;

    public OverlayDispatcher(OverlayMachine machine) => _machine = machine;

    public OverlaySnapshot Notify(NotificationRequest request)
    {
        if (QuietHours.ShouldSuppressNotify() && request.Urgency != NotifyUrgency.Error)
        {
            IslandLog.Write("dispatch", "suppressed " + request.Id + " " + QuietHours.Label);
            return _machine.Snapshot();
        }
        _queue.Enqueue(request);
        IslandLog.Write("dispatch", "notify " + request.Id + " tpl=" + request.Template + " u=" + request.Urgency);
        var kind = _machine.Snapshot().Kind;
        OverlaySnapshot snap;
        if (kind is OverlayKind.Notification or OverlayKind.Stack)
        {
            var cur = _machine.Snapshot().Payload;
            snap = _machine.Dispatch(OverlayCommand.Stack, new OverlayPayload
            {
                Title = string.IsNullOrWhiteSpace(request.Title) ? "Очередь" : request.Title,
                Subtitle = string.IsNullOrWhiteSpace(cur.Title) ? "Previous" : cur.Title,
                Line2 = string.IsNullOrWhiteSpace(request.Body) ? request.Title : request.Title + " · " + request.Body,
                Template = NotifyTemplates.Queue,
                AppId = request.AppId,
                DurationMs = request.DurationMs,
                Urgency = request.Urgency
            });
        }
        else
        {
            _machine.NotifyDurationMs = request.DurationMs;
            snap = _machine.Dispatch(OverlayCommand.Notify, ToPayload(request));
        }
        Changed?.Invoke();
        return snap;
    }

    public OverlaySnapshot Dismiss(NotificationId id)
    {
        var wasFront = _queue.Front?.Request.Id.Value == id.Value;
        _queue.Dismiss(id);
        IslandLog.Write("dispatch", "dismiss " + id);
        ActionInvoked?.Invoke(new NotificationEvent { Id = id, Action = NotificationAction.Dismiss });
        if (wasFront || _machine.Snapshot().Kind is OverlayKind.Notification or OverlayKind.Stack)
        {
            var next = _queue.Front;
            if (next is null)
                _machine.Dispatch(OverlayCommand.Clear);
            else
            {
                _machine.NotifyDurationMs = next.Request.DurationMs;
                _machine.Dispatch(OverlayCommand.Notify, ToPayload(next.Request));
            }
        }
        Changed?.Invoke();
        return _machine.Snapshot();
    }

    public OverlaySnapshot InvokeAction(NotificationAction action)
    {
        var front = _queue.Front;
        var id = front?.Request.Id ?? NotificationId.Parse("ui");
        IslandLog.Write("dispatch", "action " + action + " " + id);
        ActionInvoked?.Invoke(new NotificationEvent { Id = id, Action = action });
        if (action is NotificationAction.Dismiss or NotificationAction.Cancel)
            return Dismiss(id);
        Changed?.Invoke();
        return _machine.Snapshot();
    }

    public OverlaySnapshot Tick(int ms)
    {
        var before = _machine.Snapshot().Kind;
        var snap = _machine.Tick(ms);
        if (before == OverlayKind.Notification && snap.Kind != OverlayKind.Notification)
        {
            _queue.PopFront();
            var next = _queue.Front;
            if (next is not null)
            {
                _machine.NotifyDurationMs = next.Request.DurationMs;
                snap = _machine.Dispatch(OverlayCommand.Notify, ToPayload(next.Request));
                Changed?.Invoke();
            }
        }
        return snap;
    }

    private static OverlayPayload ToPayload(NotificationRequest req) => new()
    {
        Title = req.Title,
        Subtitle = req.Subtitle,
        Body = req.Body,
        Template = req.Template,
        AppId = req.AppId,
        DurationMs = req.DurationMs,
        Progress = req.Progress,
        EtaSeconds = req.EtaSeconds,
        Cancellable = req.Cancellable,
        Urgency = req.Urgency
    };
}
