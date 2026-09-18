using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace NotifyIsland;

internal static class ToastHub
{
    public static string Status { get; private set; } = "Off";
    public static string Probe { get; private set; } = "";
    public static string Detail { get; private set; } = "";
    public static bool Allowed => Status.Equals("Allowed", StringComparison.OrdinalIgnoreCase);

    public static async Task RefreshAsync(bool listen)
    {
        if (!listen)
        {
            Status = "Off";
            Detail = "Listener off";
            return;
        }
        try
        {
            Probe = ToastIdentity.ProbePackage();
            var listener = Windows.UI.Notifications.Management.UserNotificationListener.Current;
            var access = await listener.RequestAccessAsync();
            Status = access.ToString();
            Detail = "RequestAccessAsync=" + access + " · identity=" + Probe;
            if (access != Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Allowed)
                return;
            listener.NotificationChanged -= OnChanged;
            listener.NotificationChanged += OnChanged;
        }
        catch (Exception ex)
        {
            Status = "Unavailable (" + ex.GetType().Name + ")";
            Detail = Status + " · identity=" + ToastIdentity.ProbePackage();
        }
    }

    private static async void OnChanged(Windows.UI.Notifications.Management.UserNotificationListener sender, object args)
    {
        try
        {
            var all = await sender.GetNotificationsAsync(Windows.UI.Notifications.NotificationKinds.Toast);
            var last = all?.LastOrDefault();
            if (last is null) return;
            var app = last.AppInfo?.DisplayInfo?.DisplayName ?? "Notification";
            var aumid = "";
            try { aumid = last.AppInfo?.AppUserModelId ?? ""; } catch { }
            string body = "";
            try
            {
                var texts = last.Notification?.Visual?.GetBinding(Windows.UI.Notifications.KnownNotificationBindings.ToastGeneric)?.GetTextElements();
                body = texts is null ? "" : string.Join(" · ", texts.Select(t => t.Text).Where(s => !string.IsNullOrWhiteSpace(s)).Take(2));
            }
            catch
            {
            }
            Dispatcher.UIThread.Post(() => IslandHost.Overlay?.ShowToast(app, body, NotifyTemplates.Guess(app, aumid)));
            _ = AppBadgeHub.RefreshAsync();
        }
        catch
        {
        }
    }
}
