using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace NotifyIsland;

/// <summary>
/// Live Now Playing via Windows System Media Transport Controls (SMTC).
/// Fail-soft: if WinRT is unavailable or no session, raises null and leaves demo/idle alone.
/// </summary>
public sealed class WindowsMediaSessionSource : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private System.Threading.Timer? _poll;
    private int _started;
    private int _refreshing;
    private bool _disposed;
    private string _lastFingerprint = "";
    private byte[]? _lastArtwork;
    private string _lastArtworkKey = "";

    public event Action<MediaSessionSnapshot?>? Changed;

    public bool IsAvailable { get; private set; }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;
        try
        {
            ct.ThrowIfCancellationRequested();
            AppLog.Warn("WindowsMediaSessionSource RequestAsync…");
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(TimeSpan.FromSeconds(8));
            var token = linked.Token;
            var mgrTask = RequestManagerAsync(token);
            _manager = await mgrTask.ConfigureAwait(false);
            if (_manager is null)
            {
                Interlocked.Exchange(ref _started, 0);
                IsAvailable = false;
                AppLog.Warn("WindowsMediaSessionSource RequestAsync returned null — Now Playing idle");
                Changed?.Invoke(null);
                return;
            }
            IsAvailable = true;
            _manager.CurrentSessionChanged += OnCurrentSessionChanged;
            _manager.SessionsChanged += OnSessionsChanged;
            await BindSessionAsync(_manager.GetCurrentSession()).ConfigureAwait(false);
            // Timeline progress needs a light poll (SMTC timeline events are sparse).
            _poll = new System.Threading.Timer(_ => _ = RefreshAsync(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            await RefreshAsync().ConfigureAwait(false);
            AppLog.Warn("WindowsMediaSessionSource started (SMTC)");
        }
        catch (OperationCanceledException)
        {
            IsAvailable = false;
            Interlocked.Exchange(ref _started, 0);
            AppLog.Warn("WindowsMediaSessionSource RequestAsync timed out/canceled — Now Playing idle");
            Changed?.Invoke(null);
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            Interlocked.Exchange(ref _started, 0);
            AppLog.Warn("WindowsMediaSessionSource unavailable — Now Playing idle", ex);
            Changed?.Invoke(null);
        }
    }

    private static async Task<GlobalSystemMediaTransportControlsSessionManager?> RequestManagerAsync(CancellationToken ct)
    {
        var op = GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        // Poll completion so we can honor CancelAfter without AsTask package.
        while (op.Status == Windows.Foundation.AsyncStatus.Started)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(50, ct).ConfigureAwait(false);
        }
        ct.ThrowIfCancellationRequested();
        if (op.Status != Windows.Foundation.AsyncStatus.Completed)
            return null;
        return op.GetResults();
    }

    public void Stop()
    {
        try
        {
            _poll?.Dispose();
            _poll = null;
            UnhookSession();
            if (_manager is not null)
            {
                _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
                _manager.SessionsChanged -= OnSessionsChanged;
                _manager = null;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("WindowsMediaSessionSource.Stop failed", ex);
        }
        Interlocked.Exchange(ref _started, 0);
        IsAvailable = false;
        _lastFingerprint = "";
        Changed?.Invoke(null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    public async Task TryTogglePlayPauseAsync()
    {
        try
        {
            var s = _session;
            if (s is null) return;
            await s.TryTogglePlayPauseAsync();
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Warn("TryTogglePlayPauseAsync failed", ex);
        }
    }

    public async Task TrySkipNextAsync()
    {
        try
        {
            var s = _session;
            if (s is null) return;
            await s.TrySkipNextAsync();
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Warn("TrySkipNextAsync failed", ex);
        }
    }

    public async Task TrySkipPreviousAsync()
    {
        try
        {
            var s = _session;
            if (s is null) return;
            await s.TrySkipPreviousAsync();
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Warn("TrySkipPreviousAsync failed", ex);
        }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) =>
        _ = OnSessionListChangedAsync();

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) =>
        _ = OnSessionListChangedAsync();

    private async Task OnSessionListChangedAsync()
    {
        try
        {
            var mgr = _manager;
            if (mgr is null) return;
            await BindSessionAsync(mgr.GetCurrentSession()).ConfigureAwait(false);
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Warn("OnSessionListChangedAsync failed", ex);
        }
    }

    private Task BindSessionAsync(GlobalSystemMediaTransportControlsSession? session)
    {
        UnhookSession();
        _session = session;
        _lastFingerprint = "";
        _lastArtwork = null;
        _lastArtworkKey = "";
        if (session is null) return Task.CompletedTask;
        session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
        return Task.CompletedTask;
    }

    private void UnhookSession()
    {
        if (_session is null) return;
        try
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }
        catch { /* ignore */ }
        _session = null;
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) =>
        _ = RefreshAsync(forceArtwork: true);

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) =>
        _ = RefreshAsync();

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) =>
        _ = RefreshAsync();

    private async Task RefreshAsync(bool forceArtwork = false)
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1) return;
        try
        {
            var session = _session ?? _manager?.GetCurrentSession();
            if (session is null)
            {
                if (_lastFingerprint != "")
                {
                    _lastFingerprint = "";
                    Changed?.Invoke(null);
                }
                return;
            }

            if (!ReferenceEquals(session, _session))
                await BindSessionAsync(session).ConfigureAwait(false);

            var props = await session.TryGetMediaPropertiesAsync();
            var title = props?.Title?.Trim() ?? "";
            var artist = props?.Artist?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(artist))
                artist = props?.AlbumTitle?.Trim() ?? "";

            var playback = session.GetPlaybackInfo();
            var playing = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            var timeline = session.GetTimelineProperties();
            double progress = 0;
            if (timeline is not null)
            {
                var start = timeline.StartTime.TotalSeconds;
                var end = timeline.EndTime.TotalSeconds;
                var pos = timeline.Position.TotalSeconds;
                var dur = end - start;
                if (dur > 0.5 && !double.IsNaN(pos) && !double.IsInfinity(pos))
                    progress = Math.Clamp((pos - start) / dur, 0, 1);
            }

            var artKey = $"{title}|{artist}|{props?.AlbumTitle}";
            if (forceArtwork || artKey != _lastArtworkKey)
            {
                _lastArtworkKey = artKey;
                _lastArtwork = await TryReadThumbnailAsync(props).ConfigureAwait(false);
            }

            var snap = new MediaSessionSnapshot
            {
                Title = string.IsNullOrWhiteSpace(title) ? "Без названия" : title,
                Artist = string.IsNullOrWhiteSpace(artist) ? "Неизвестный исполнитель" : artist,
                Playing = playing,
                Progress = progress,
                ArtworkBytes = _lastArtwork,
                SourceAppId = session.SourceAppUserModelId ?? ""
            };

            var fp = $"{snap.Title}|{snap.Artist}|{snap.Playing}|{snap.Progress:0.000}|{snap.ArtworkBytes?.Length ?? 0}";
            if (fp == _lastFingerprint) return;
            _lastFingerprint = fp;
            Changed?.Invoke(snap);
        }
        catch (Exception ex)
        {
            AppLog.Warn("WindowsMediaSessionSource.RefreshAsync failed", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    private static async Task<byte[]?> TryReadThumbnailAsync(GlobalSystemMediaTransportControlsSessionMediaProperties? props)
    {
        if (props?.Thumbnail is null) return null;
        try
        {
            using var stream = await props.Thumbnail.OpenReadAsync();
            if (stream.Size is 0 or > 2_000_000) return null;
            var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)stream.Size);
            var bytes = new byte[stream.Size];
            reader.ReadBytes(bytes);
            reader.Dispose();
            return bytes.Length > 0 ? bytes : null;
        }
        catch (Exception ex)
        {
            AppLog.Warn("TryReadThumbnailAsync failed", ex);
            return null;
        }
    }
}

/// <summary>UI-facing snapshot of the current SMTC session (no WinRT types leaked to Core).</summary>
public sealed class MediaSessionSnapshot
{
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public bool Playing { get; init; }
    public double Progress { get; init; }
    public byte[]? ArtworkBytes { get; init; }
    public string SourceAppId { get; init; } = "";

    public OverlayPayload ToPayload() => new()
    {
        Title = Title,
        Subtitle = Artist,
        Progress = Progress,
        Playing = Playing,
        ArtworkBytes = ArtworkBytes
    };
}
