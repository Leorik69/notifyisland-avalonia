namespace NotifyIsland.Core;

public readonly record struct NotificationId(string Value)
{
    public override string ToString() => Value;

    public static NotificationId New() => new(Guid.NewGuid().ToString("N"));

    public static NotificationId Parse(string? raw)
    {
        var s = (raw ?? "").Trim();
        return s.Length == 0 ? New() : new(s);
    }
}

public enum NotificationAction
{
    None,
    Content,
    Dismiss,
    Mute,
    Cancel
}

public enum NotifyUrgency
{
    Normal,
    Success,
    Warning,
    Error
}

public sealed class NotificationRequest
{
    public NotificationId Id { get; init; } = NotificationId.New();
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string Body { get; init; } = "";
    public string Template { get; init; } = "notify";
    public string AppId { get; init; } = "";
    public int DurationMs { get; init; } = 4000;
    public bool Replace { get; init; } = true;
    public NotificationAction DefaultAction { get; init; } = NotificationAction.Content;
    public NotifyUrgency Urgency { get; init; } = NotifyUrgency.Normal;
    public double Progress { get; init; }
    public double EtaSeconds { get; init; }
    public bool Cancellable { get; init; }
}

public sealed class NotificationRecord
{
    public required NotificationRequest Request { get; init; }
    public DateTimeOffset EnqueuedUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class NotificationEvent
{
    public required NotificationId Id { get; init; }
    public required NotificationAction Action { get; init; }
}
