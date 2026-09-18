using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Windows.UI.Notifications.Management;

namespace NotifyIsland;

internal sealed class AppBadgeSnapshot
{
    public string Aumid { get; init; } = "";
    public string Name { get; init; } = "";
    public int Count { get; init; }
    public Bitmap? Logo { get; init; }
    public bool Preview { get; init; }
}

internal static class AppBadgeHub
{
    public static AppBadgeSnapshot? Current { get; private set; }
    public static event Action? Changed;

    private static AppBadgeSnapshot? _preview;
    private static readonly Dictionary<string, Bitmap> LogoCache = new(StringComparer.OrdinalIgnoreCase);

    public static void SetPreview(string name, int count)
    {
        _preview = new AppBadgeSnapshot { Name = name, Count = Math.Clamp(count, 1, 99), Preview = true };
        Raise();
    }

    public static void ClearPreview()
    {
        _preview = null;
        Raise();
    }

    public static async Task RefreshAsync()
    {
        var prefs = PrefsStore.Current;
        if (!prefs.ShowAppBadge)
        {
            Current = _preview;
            Raise();
            return;
        }
        if (_preview is not null)
        {
            Current = _preview;
            Raise();
            return;
        }
        if (!prefs.ListenToasts || !ToastHub.Allowed)
        {
            Current = null;
            Raise();
            return;
        }
        try
        {
            var listener = UserNotificationListener.Current;
            var all = await listener.GetNotificationsAsync(Windows.UI.Notifications.NotificationKinds.Toast);
            if (all is null || all.Count == 0)
            {
                Current = null;
                Raise();
                return;
            }
            var groups = all
                .Select(n =>
                {
                    var id = "";
                    var name = "App";
                    try
                    {
                        id = n.AppInfo?.AppUserModelId ?? "";
                        name = n.AppInfo?.DisplayInfo?.DisplayName ?? "App";
                    }
                    catch
                    {
                    }
                    return (n, id, name);
                })
                .Where(x => AllowApp(prefs, x.id, x.name))
                .GroupBy(x => string.IsNullOrWhiteSpace(x.id) ? x.name : x.id)
                .OrderByDescending(g => g.Count())
                .ToList();
            if (groups.Count == 0)
            {
                Current = null;
                Raise();
                return;
            }
            var top = groups[0];
            var total = groups.Sum(g => g.Count());
            var sample = top.First().n;
            Bitmap? logo = null;
            try
            {
                var aumid = top.Key;
                if (LogoCache.TryGetValue(aumid, out var cached))
                    logo = cached;
                else
                {
                    var info = sample.AppInfo?.DisplayInfo;
                    if (info is not null)
                    {
                        var refer = info.GetLogo(new Windows.Foundation.Size(48, 48));
                        using var ras = await refer.OpenReadAsync();
                        var reader = new Windows.Storage.Streams.DataReader(ras);
                        await reader.LoadAsync((uint)ras.Size);
                        var bytes = new byte[ras.Size];
                        reader.ReadBytes(bytes);
                        reader.Dispose();
                        logo = new Bitmap(new MemoryStream(bytes));
                        LogoCache[aumid] = logo;
                    }
                }
            }
            catch
            {
            }
            Current = new AppBadgeSnapshot
            {
                Aumid = top.Key,
                Name = top.First().name,
                Count = total,
                Logo = logo,
                Preview = false
            };
            Raise();
        }
        catch
        {
            Current = null;
            Raise();
        }
    }

    private static bool AllowApp(UserPrefs prefs, string aumid, string name)
    {
        var filter = (prefs.BadgeApps ?? "").Trim();
        if (string.IsNullOrWhiteSpace(filter) || filter == "*") return true;
        foreach (var part in filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (aumid.Contains(part, StringComparison.OrdinalIgnoreCase) ||
                name.Contains(part, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void Raise() => Dispatcher.UIThread.Post(() => Changed?.Invoke());
}
