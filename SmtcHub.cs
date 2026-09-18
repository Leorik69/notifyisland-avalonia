using System;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace NotifyIsland;

internal sealed class SmtcSnapshot
{
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public bool Playing { get; init; }
    public bool HasSession { get; init; }
}

internal static class SmtcHub
{
    public static SmtcSnapshot Current { get; private set; } = new();
    public static string Status { get; private set; } = "No session";

    public static async Task<SmtcSnapshot> RefreshAsync()
    {
        try
        {
            var mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = mgr.GetCurrentSession();
            if (session is null)
            {
                Current = new SmtcSnapshot();
                Status = "No SMTC session";
                return Current;
            }
            var props = await session.TryGetMediaPropertiesAsync();
            var pb = session.GetPlaybackInfo();
            Current = new SmtcSnapshot
            {
                HasSession = true,
                Title = string.IsNullOrWhiteSpace(props?.Title) ? "Now playing" : props!.Title,
                Artist = props?.Artist ?? "",
                Playing = pb?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
            };
            Status = Current.Title;
            return Current;
        }
        catch (Exception ex)
        {
            Status = "Unavailable (" + ex.GetType().Name + ")";
            Current = new SmtcSnapshot();
            return Current;
        }
    }

    public static async Task TogglePlayAsync()
    {
        try
        {
            var mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = mgr.GetCurrentSession();
            if (session is null) return;
            await session.TryTogglePlayPauseAsync();
            await RefreshAsync();
        }
        catch
        {
        }
    }

    public static async Task NextAsync()
    {
        try
        {
            var mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = mgr.GetCurrentSession();
            if (session is null) return;
            await session.TrySkipNextAsync();
            await RefreshAsync();
        }
        catch
        {
        }
    }
}
