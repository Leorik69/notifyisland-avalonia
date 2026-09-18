using System.Collections.Generic;

namespace NotifyIsland.Core;

public sealed class NotificationQueue
{
    private readonly List<NotificationRecord> _items = new();

    public int Count => _items.Count;

    public NotificationRecord? Front => _items.Count == 0 ? null : _items[0];

    public void Enqueue(NotificationRequest request)
    {
        if (request.Replace)
        {
            for (var i = 0; i < _items.Count; i++)
            {
                if (_items[i].Request.Id.Value == request.Id.Value)
                {
                    _items[i] = new NotificationRecord { Request = request };
                    return;
                }
            }
        }
        _items.Add(new NotificationRecord { Request = request });
    }

    public bool Dismiss(NotificationId id)
    {
        var n = _items.RemoveAll(x => x.Request.Id.Value == id.Value);
        return n > 0;
    }

    public NotificationRecord? PopFront()
    {
        if (_items.Count == 0) return null;
        var item = _items[0];
        _items.RemoveAt(0);
        return item;
    }

    public void Clear() => _items.Clear();
}
