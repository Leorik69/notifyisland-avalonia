using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace NotifyIsland;

public partial class OverlayWindow : Window
{
    private readonly OverlayMachine _machine = new();
    private readonly AppSettings _settings;
    private readonly WindowsWeatherSource _weather;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _demo = new() { Interval = TimeSpan.FromSeconds(1.8) };
    private readonly DispatcherTimer _weatherTimer = new() { Interval = TimeSpan.FromMilliseconds(OverlayTokens.WeatherRefreshMs) };
    private bool _demoOn;
    private CancellationTokenSource? _weatherCts;

    private Point _pressOrigin;
    private PixelPoint _dragOriginPos;
    private bool _pressing;
    private bool _dragging;
    private bool _weatherIconFlip;
    private string _lastWeatherIconKey = "";
    private readonly TranslateTransform _pillTranslate = new();
    private readonly Stopwatch _pressWatch = new();
    private OverlayKind _lastKind = OverlayKind.Idle;
    private SettingsWindow? _settingsWindow;
    private TrayService? _tray;
    private WinFormsTray? _winTray;
    private int _lastTrayUnread = -1;
    private Color _pillFill = Color.Parse("#080808");
    private double _idleFillA = 1.0;

    public AppSettings Settings => _settings;

    public OverlayWindow()
    {
        InitializeComponent();
        Pill.RenderTransform = _pillTranslate;
        _settings = AppSettings.Load();
        _settings.Normalize();
        _machine.WeatherEnabled = _settings.WeatherEnabled;
        _weather = new WindowsWeatherSource(_settings.Latitude, _settings.Longitude);
        _pillFill = Color.Parse(OverlayTokens.FillHex);
        _idleFillA = _settings.Opacity;

        EnableMorphTransitions();
        WirePointerGestures();
        SeedIcons();
        ApplyWeatherSide();
        ApplyOpacity();
        ApplyIslandVisibility();

        // Prefer WinForms NotifyIcon (visible on Win11 Sandbox); Avalonia TrayIcon as fallback.
        try
        {
            _winTray ??= new WinFormsTray(this);
            _winTray.RefreshIcon(_machine.UnreadCount);
            AppLog.Warn("Using WinFormsTray as primary tray");
        }
        catch (Exception ex)
        {
            AppLog.Warn("WinFormsTray ctor failed — falling back to Avalonia TrayService", ex);
            try
            {
                _tray ??= new TrayService(this);
                _tray.RefreshIcon(_machine.UnreadCount);
            }
            catch (Exception ex2)
            {
                AppLog.Warn("TrayService ctor failed", ex2);
            }
        }

        Opened += (_, _) =>
        {
            Win32Overlay.ApplyNoActivate(this);
            Win32Overlay.ApplyZOrder(this, _settings.ZOrderMode);
            PlaceIsland();
            _ = RefreshWeatherAsync();
            if (_winTray is null && _tray is null)
            {
                try
                {
                    _tray ??= new TrayService(this);
                    _tray.RefreshIcon(_machine.UnreadCount);
                }
                catch (Exception ex)
                {
                    AppLog.Warn("TrayService Opened init failed", ex);
                }
            }
        };
        KeyDown += OnKey;
        _clock.Tick += (_, _) => TickClock();
        _tick.Tick += (_, _) =>
        {
            var before = _machine.Snapshot().Kind;
            _machine.Tick(200);
            var after = _machine.Snapshot().Kind;
            if (before != after) OnKindChanged(before, after);
            Paint();
            if (before != after) ApplySize();
            var unread = _machine.UnreadCount;
            if (unread != _lastTrayUnread)
            {
                _lastTrayUnread = unread;
                _tray?.RefreshIcon(unread);
                _winTray?.RefreshIcon(unread);
            }
        };
        _demo.Tick += (_, _) =>
        {
            var before = _machine.Snapshot().Kind;
            _machine.Dispatch(OverlayCommand.DemoNext);
            var after = _machine.Snapshot().Kind;
            if (before != after) OnKindChanged(before, after);
            ApplySize();
            Paint();
        };
        _weatherTimer.Tick += (_, _) => _ = RefreshWeatherAsync();

        _clock.Start();
        _tick.Start();
        _weatherTimer.Start();
        TickClock();
        ApplySize();
        Paint();
        if (Program.DemoMode) StartDemo();
        if (Program.SettingsMode)
            Dispatcher.UIThread.Post(OpenSettings, DispatcherPriority.Background);
    }

    private void SeedIcons()
    {
        ClockIconHost.Child = IslandIcons.Create("clock", OverlayTokens.IconSizeCollapsed,
            new SolidColorBrush(Color.Parse(OverlayTokens.TextSecondaryHex)));
        SetKindIcon(OverlayKind.Idle);
        SetWeatherIcons(WeatherCodes.IconKey(_machine.LastWeather.WeatherCode ?? 0), animate: false);
    }

    private void EnableMorphTransitions()
    {
        var duration = TimeSpan.FromMilliseconds(OverlayTokens.MorphMs);
        var softOut = new CubicEaseOut();
        var softInOut = new CubicEaseInOut();
        var fade = TimeSpan.FromMilliseconds(OverlayTokens.IconCrossfadeMs);

        Transitions = new Transitions
        {
            new DoubleTransition { Property = WidthProperty, Duration = duration, Easing = softOut },
            new DoubleTransition { Property = HeightProperty, Duration = duration, Easing = softOut },
        };
        Pill.Transitions = new Transitions
        {
            new DoubleTransition { Property = Border.WidthProperty, Duration = duration, Easing = softOut },
            new DoubleTransition { Property = Border.HeightProperty, Duration = duration, Easing = softOut },
            new BrushTransition { Property = Border.BorderBrushProperty, Duration = TimeSpan.FromMilliseconds(160), Easing = softInOut },
            new BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(160), Easing = softInOut },
        };
        UnreadDot.Transitions = new Transitions
        {
            new DoubleTransition { Property = OpacityProperty, Duration = duration, Easing = softOut },
        };
        WeatherIconA.Transitions = new Transitions
        {
            new DoubleTransition { Property = OpacityProperty, Duration = fade, Easing = softOut },
        };
        WeatherIconB.Transitions = new Transitions
        {
            new DoubleTransition { Property = OpacityProperty, Duration = fade, Easing = softOut },
        };
        _pillTranslate.Transitions = new Transitions
        {
            new DoubleTransition { Property = TranslateTransform.XProperty, Duration = TimeSpan.FromMilliseconds(OverlayTokens.SwipeRubberMs), Easing = softOut },
            new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(OverlayTokens.SwipeRubberMs), Easing = softOut },
        };

        Pill.PointerEntered += (_, _) =>
        {
            Pill.BorderBrush = new SolidColorBrush(Color.Parse("#55FFFFFF"));
            Pill.Background = new SolidColorBrush(WithAlpha(_pillFill, Math.Min(1.0, _idleFillA + 0.08)));
            IslandSounds.Play(IslandSoundKind.Hover, _settings);
        };
        Pill.PointerExited += (_, _) =>
        {
            Pill.BorderBrush = new SolidColorBrush(Color.Parse("#28FFFFFF"));
            ApplyOpacity();
        };
    }

    private static Color WithAlpha(Color c, double a) =>
        Color.FromArgb((byte)Math.Clamp((int)Math.Round(a * 255), 0, 255), c.R, c.G, c.B);

    private void ApplyOpacity()
    {
        _idleFillA = Math.Clamp(_settings.Opacity, 0.35, 1.0);
        Pill.Background = new SolidColorBrush(WithAlpha(_pillFill, _idleFillA));
    }

    private void ApplyWeatherSide()
    {
        // Reorder: clock block vs weather — Left = weather before clock
        var row = CollapsedRow;
        var clockIcon = ClockIconHost;
        var clockText = ClockText;
        var weather = MinimalWeather;
        var dot = UnreadDot;
        row.Children.Clear();
        if (_settings.WeatherSide == WeatherSide.Left)
        {
            row.Children.Add(weather);
            row.Children.Add(clockIcon);
            row.Children.Add(clockText);
            row.Children.Add(dot);
        }
        else
        {
            row.Children.Add(clockIcon);
            row.Children.Add(clockText);
            row.Children.Add(weather);
            row.Children.Add(dot);
        }
    }

    private void ApplyOrientationLayout()
    {
        var vertical = IslandLayout.IsVertical(_settings.Orientation, _settings.Edge);
        CollapsedRow.Orientation = vertical ? Avalonia.Layout.Orientation.Vertical : Avalonia.Layout.Orientation.Horizontal;
        MinimalWeather.Orientation = vertical ? Avalonia.Layout.Orientation.Vertical : Avalonia.Layout.Orientation.Horizontal;
    }

    private void WirePointerGestures()
    {
        Pill.PointerPressed += OnPillPointerPressed;
        Pill.PointerMoved += OnPillPointerMoved;
        Pill.PointerReleased += OnPillPointerReleased;
        Pill.PointerCaptureLost += (_, _) => { ResetSwipeVisual(); _dragging = false; };
    }

    private void OnPillPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed)
        {
            OpenContextMenu();
            e.Handled = true;
            return;
        }

        if (!props.IsLeftButtonPressed) return;

        _pressOrigin = e.GetPosition(this);
        _dragOriginPos = Position;
        _pressing = true;
        _dragging = false;
        _pressWatch.Restart();
        e.Pointer.Capture(Pill);
        e.Handled = true;
    }

    private void OnPillPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_pressing) return;
        var pos = e.GetPosition(this);
        var dx = pos.X - _pressOrigin.X;
        var dy = pos.Y - _pressOrigin.Y;

        if (_settings.AllowDrag && !_dragging && _pressWatch.ElapsedMilliseconds >= IslandLayout.DragHoldMs)
        {
            // Convert to drag-reposition; cancel swipe rubber-band
            _dragging = true;
            _pillTranslate.X = 0;
            _pillTranslate.Y = 0;
        }

        if (_dragging)
        {
            var scale = RenderScaling <= 0 ? 1 : RenderScaling;
            var nx = _dragOriginPos.X + (int)Math.Round(dx * scale);
            var ny = _dragOriginPos.Y + (int)Math.Round(dy * scale);
            Position = new PixelPoint(nx, ny);
            e.Handled = true;
            return;
        }

        var damp = 0.45;
        _pillTranslate.X = Math.Clamp(dx * damp, -56, 56);
        _pillTranslate.Y = Math.Clamp(dy * damp, -40, 40);
    }

    private void OnPillPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_pressing) return;
        _pressing = false;
        e.Pointer.Capture(null);
        _pressWatch.Stop();

        var pos = e.GetPosition(this);
        var dx = pos.X - _pressOrigin.X;
        var dy = pos.Y - _pressOrigin.Y;
        var adx = Math.Abs(dx);
        var ady = Math.Abs(dy);
        var dist = Math.Sqrt(dx * dx + dy * dy);

        if (_dragging)
        {
            _dragging = false;
            ResetSwipeVisual();
            CommitDragOffsets();
            e.Handled = true;
            return;
        }

        ResetSwipeVisual();

        if (dist <= OverlayTokens.SwipeClickMaxPx)
        {
            var kind = _machine.Snapshot().Kind;
            if (kind is OverlayKind.Idle or OverlayKind.Collapsed)
                TrayService.OpenActionCenter();
            e.Handled = true;
            return;
        }

        if (dist < OverlayTokens.SwipeFirePx)
        {
            e.Handled = true;
            return;
        }

        var before = _machine.Snapshot().Kind;
        if (adx >= ady)
            _machine.Dispatch(dx < 0 ? OverlayCommand.CycleNext : OverlayCommand.CyclePrev);
        else if (dy > 0)
            _machine.Dispatch(OverlayCommand.Collapse);
        else
            _machine.Dispatch(OverlayCommand.ExpandWidget);

        var after = _machine.Snapshot().Kind;
        IslandSounds.Play(IslandSoundKind.Swipe, _settings);
        if (before != after) OnKindChanged(before, after);
        ApplySize();
        Paint();
        e.Handled = true;
    }

    private void CommitDragOffsets()
    {
        var screen = Screens.Primary ?? Screens.ScreenFromWindow(this);
        if (screen is null) return;
        var wa = screen.WorkingArea;
        var scale = RenderScaling <= 0 ? 1 : RenderScaling;
        var pw = (int)Math.Round(Width * scale);
        var ph = (int)Math.Round(Height * scale);
        var (ox, oy) = IslandLayout.OffsetsFromPosition(
            wa.X, wa.Y, wa.Width, wa.Height, pw, ph,
            _settings.Edge, Position.X, Position.Y);
        _settings.OffsetX = ox;
        _settings.OffsetY = oy;
        _settings.Save();
        // Refresh Numeric fields if settings open — user sees new offsets next open
    }

    private void ResetSwipeVisual()
    {
        _pillTranslate.X = 0;
        _pillTranslate.Y = 0;
        _pressing = false;
    }

    private void OpenContextMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Menu("Demo F9", () => { if (_demoOn) StopDemo(); else StartDemo(); }));
        var weatherLabel = _settings.WeatherEnabled ? "Погода выкл" : "Погода вкл";
        menu.Items.Add(Menu(weatherLabel, ToggleWeather));
        menu.Items.Add(Menu("Свернуть", () =>
        {
            var before = _machine.Snapshot().Kind;
            _machine.Dispatch(OverlayCommand.Collapse);
            OnKindChanged(before, _machine.Snapshot().Kind);
            ApplySize(); Paint();
        }));
        menu.Items.Add(Menu("Настройки…", OpenSettings));
        menu.Items.Add(Menu("Выход", () =>
            (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown()));
        menu.Open(Pill);
    }

    public void OpenSettings()
    {
        try
        {
            if (_settingsWindow is { IsVisible: true })
            {
                _settingsWindow.Activate();
                _settingsWindow.Topmost = true;
                _settingsWindow.Topmost = false;
                return;
            }

            _settingsWindow = new SettingsWindow(_settings, ApplySettingsFromUi);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        catch (Exception ex)
        {
            AppLog.Warn("OpenSettings failed", ex);
            throw;
        }
    }

    private void ApplySettingsFromUi(AppSettings draft)
    {
        draft.CopyTo(_settings);
        _settings.Normalize();
        _settings.Save();
        _machine.WeatherEnabled = _settings.WeatherEnabled;
        if (!_settings.WeatherEnabled && _machine.Snapshot().Kind == OverlayKind.Weather)
            _machine.Dispatch(OverlayCommand.Collapse);
        ApplyWeatherSide();
        ApplyOrientationLayout();
        ApplyOpacity();
        ApplyIslandVisibility();
        Win32Overlay.ApplyZOrder(this, _settings.ZOrderMode);
        ApplySize();
        Paint();
        _tray?.RefreshLabels();
        _winTray?.RefreshLabels();
        _tray?.RefreshIcon(_machine.UnreadCount);
            _winTray?.RefreshIcon(_machine.UnreadCount);
        if (_settings.WeatherEnabled)
            _ = RefreshWeatherAsync();
    }

    public void ToggleIslandVisible()
    {
        _settings.IslandVisible = !_settings.IslandVisible;
        _settings.Save();
        ApplyIslandVisibility();
        _tray?.RefreshLabels();
        _winTray?.RefreshLabels();
    }

    public void ToggleWeatherFromTray() => ToggleWeather();

    public bool IsDemoRunning => _demoOn;

    public void ToggleDemoFromTray()
    {
        if (_demoOn) StopDemo();
        else StartDemo();
        _tray?.RefreshLabels();
        _winTray?.RefreshLabels();
    }

    private void ApplyIslandVisibility()
    {
        // Hide without closing — keep tray/settings alive
        Opacity = _settings.IslandVisible ? 1 : 0;
        IsHitTestVisible = _settings.IslandVisible;
        ShowInTaskbar = false;
        if (_settings.IslandVisible)
        {
            Show();
            Win32Overlay.ApplyNoActivate(this);
            Win32Overlay.ApplyZOrder(this, _settings.ZOrderMode);
            PlaceIsland();
        }
        else
        {
            // Keep window open but invisible; alternative Hide() breaks some tray hosts
            // Use Hide for true disappearance from hit-testing/DWM
            Hide();
        }
    }

    private void ToggleWeather()
    {
        _settings.WeatherEnabled = !_settings.WeatherEnabled;
        _settings.Save();
        _machine.WeatherEnabled = _settings.WeatherEnabled;
        if (!_settings.WeatherEnabled && _machine.Snapshot().Kind == OverlayKind.Weather)
            _machine.Dispatch(OverlayCommand.Collapse);
        ApplySize();
        Paint();
        _tray?.RefreshLabels();
        _winTray?.RefreshLabels();
        if (_settings.WeatherEnabled)
            _ = RefreshWeatherAsync();
    }

    private void OnKindChanged(OverlayKind before, OverlayKind after)
    {
        var wasCollapsed = before is OverlayKind.Idle or OverlayKind.Collapsed;
        var nowCollapsed = after is OverlayKind.Idle or OverlayKind.Collapsed;
        if (after == OverlayKind.Notification)
            IslandSounds.Play(IslandSoundKind.Notify, _settings);
        else if (after == OverlayKind.Error)
            IslandSounds.Play(IslandSoundKind.Error, _settings);
        else if (wasCollapsed && !nowCollapsed)
            IslandSounds.Play(IslandSoundKind.Expand, _settings);
        else if (!wasCollapsed && nowCollapsed)
            IslandSounds.Play(IslandSoundKind.Collapse, _settings);
        _lastKind = after;
    }

    private async Task RefreshWeatherAsync()
    {
        _weatherCts?.Cancel();
        _weatherCts = new CancellationTokenSource();
        var ct = _weatherCts.Token;
        try
        {
            var payload = await _weather.GetWeatherAsync(ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested) return;
            _machine.UpdateWeatherCache(payload);
            ApplySize();
            Paint();
        }
        catch (Exception ex)
        {
            AppLog.Warn("RefreshWeatherAsync failed", ex);
            _machine.UpdateWeatherCache(WeatherCodes.MockMoscow());
            ApplySize();
            Paint();
        }
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F9) { if (_demoOn) StopDemo(); else StartDemo(); e.Handled = true; }
        else if (e.Key == Key.Escape)
        {
            var before = _machine.Snapshot().Kind;
            _machine.Dispatch(OverlayCommand.Collapse);
            OnKindChanged(before, _machine.Snapshot().Kind);
            ApplySize(); Paint(); e.Handled = true;
        }
    }

    private void StartDemo()
    {
        _demoOn = true;
        _machine.Dispatch(OverlayCommand.Clear);
        var before = _machine.Snapshot().Kind;
        _machine.Dispatch(OverlayCommand.Notify, new OverlayPayload { Title = "Сообщение", Body = "Демо уведомление" });
        OnKindChanged(before, _machine.Snapshot().Kind);
        ApplySize(); Paint();
        _demo.Interval = TimeSpan.FromMilliseconds(OverlayTokens.DefaultNotifyMs + 1200);
        _demo.Start();
        void OnFirst(object? s, EventArgs e)
        {
            _demo.Tick -= OnFirst;
            _demo.Interval = TimeSpan.FromSeconds(1.8);
        }
        _demo.Tick += OnFirst;
    }

    private void StopDemo()
    {
        _demoOn = false; _demo.Stop();
        _machine.Dispatch(OverlayCommand.Clear); ApplySize(); Paint();
    }

    private void TickClock()
    {
        ClockText.Text = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
        var kind = _machine.Snapshot().Kind;
        if (kind is OverlayKind.Idle or OverlayKind.Collapsed)
            CollapsedRow.IsVisible = true;
    }

    private void ApplySize()
    {
        var snap = _machine.Snapshot();
        ApplyOrientationLayout();
        var (w, h) = IslandLayout.SizeFor(snap.Kind, snap.WeatherEnabled, _settings.Orientation, _settings.Edge);
        Width = w;
        Height = h;
        Pill.Width = w;
        Pill.Height = h;
        var corner = Math.Min(w, h) / 2;
        Pill.CornerRadius = new CornerRadius(corner);
        PlaceIsland();
    }

    private void PlaceIsland()
    {
        var screen = Screens.Primary ?? Screens.ScreenFromWindow(this);
        if (screen is null) return;
        var wa = screen.WorkingArea;
        var scale = RenderScaling <= 0 ? 1 : RenderScaling;
        var pw = (int)Math.Round(Width * scale);
        var ph = (int)Math.Round(Height * scale);
        var (x, y) = IslandLayout.Place(
            wa.X, wa.Y, wa.Width, wa.Height, pw, ph,
            _settings.Edge, _settings.OffsetX, _settings.OffsetY);
        Position = new PixelPoint(x, y);
        Win32Overlay.ApplyZOrder(this, _settings.ZOrderMode);
    }

    private void Paint()
    {
        var snap = _machine.Snapshot();
        var kind = snap.Kind;
        var p = snap.Payload;
        var overlayOn = kind is OverlayKind.Notification or OverlayKind.Progress or OverlayKind.Media
            or OverlayKind.Timer or OverlayKind.Error or OverlayKind.Expanded or OverlayKind.Weather;

        OverlayPanel.IsVisible = overlayOn;
        CollapsedRow.IsVisible = !overlayOn;

        var showMinimalWx = !overlayOn && snap.WeatherEnabled;
        MinimalWeather.IsVisible = showMinimalWx;
        if (showMinimalWx)
        {
            var wx = snap.LastWeather;
            WeatherTempText.Text = WeatherCodes.FormatMinimalTemp(wx.TemperatureC ?? 18);
            SetWeatherIcons(WeatherCodes.IconKey(wx.WeatherCode ?? 0), animate: true);
        }

        var title = string.IsNullOrWhiteSpace(p.Title) ? Fallback(kind) : p.Title;
        var sub = string.IsNullOrWhiteSpace(p.Subtitle) ? p.Body : p.Subtitle;
        if (kind == OverlayKind.Weather)
        {
            title = string.IsNullOrWhiteSpace(p.Body)
                ? WeatherCodes.FormatExpanded(p.TemperatureC ?? snap.LastWeather.TemperatureC ?? 18,
                    p.WeatherCode ?? snap.LastWeather.WeatherCode ?? 0, p.PrecipProb ?? snap.LastWeather.PrecipProb)
                : p.Body;
            sub = "";
        }
        else if (kind == OverlayKind.Timer)
        {
            var ts = TimeSpan.FromSeconds(Math.Ceiling(p.RemainingSeconds));
            sub = $"{(int)ts.TotalMinutes:00}:{ts.Seconds:00}";
        }
        else if (kind == OverlayKind.Progress && string.IsNullOrWhiteSpace(sub))
            sub = $"{(int)Math.Round(p.Progress * 100)}%";

        OverlayTitle.Text = string.IsNullOrWhiteSpace(sub) ? title : $"{title} · {sub}";
        OverlaySubtitle.Text = "";

        OverlayProgress.Value = p.Progress * 100;
        OverlayProgress.IsVisible = kind is OverlayKind.Progress or OverlayKind.Media;

        OverlayTitle.Foreground = new SolidColorBrush(Color.Parse(kind == OverlayKind.Error ? OverlayTokens.ErrorHex : OverlayTokens.TextHex));
        AppIcon.Background = new SolidColorBrush(Color.Parse(kind == OverlayKind.Error ? OverlayTokens.ErrorHex : OverlayTokens.AccentHex));

        if (kind == OverlayKind.Weather)
            SetKindIconWeather(p.WeatherCode ?? snap.LastWeather.WeatherCode ?? 0);
        else
            SetKindIcon(kind);

        MediaPlay.IsVisible = kind == OverlayKind.Media;
        MediaPlayGlyph.Text = p.Playing ? "||" : "▶";

        var unread = snap.UnreadCount;
        var showBadge = overlayOn && unread > 0 && kind is OverlayKind.Notification or OverlayKind.Expanded;
        UnreadBadge.IsVisible = showBadge;
        BadgeText.Text = unread > 99 ? "99+" : unread.ToString(CultureInfo.InvariantCulture);

        var showDot = !overlayOn && unread > 0;
        UnreadDot.Opacity = showDot ? 1.0 : 0.0;

        ToolTip.SetTip(this, kind == OverlayKind.Idle ? "NotifyIsland" : OverlayTitle.Text);
    }

    private void SetKindIcon(OverlayKind kind)
    {
        var key = IslandIcons.KindKey(kind);
        var brush = new SolidColorBrush(Colors.White);
        AppIconHost.Child = IslandIcons.Create(key, OverlayTokens.IconSizeKind, brush, 1.5);
    }

    private void SetKindIconWeather(int code)
    {
        var key = WeatherCodes.IconKey(code);
        var brush = new SolidColorBrush(Colors.White);
        AppIconHost.Child = IslandIcons.Create(key, OverlayTokens.IconSizeKind, brush, 1.5);
    }

    private void SetWeatherIcons(string key, bool animate)
    {
        if (string.Equals(key, _lastWeatherIconKey, StringComparison.OrdinalIgnoreCase) && WeatherIconA.Child is not null)
            return;

        var brush = new SolidColorBrush(Color.Parse(OverlayTokens.TextSecondaryHex));
        var path = IslandIcons.Create(key, OverlayTokens.IconSizeCollapsed, brush);

        if (!animate || WeatherIconA.Child is null)
        {
            WeatherIconA.Child = path;
            WeatherIconA.Opacity = 1;
            WeatherIconB.Child = null;
            WeatherIconB.Opacity = 0;
            _weatherIconFlip = false;
            _lastWeatherIconKey = key;
            return;
        }

        if (!_weatherIconFlip)
        {
            WeatherIconB.Child = path;
            WeatherIconB.Opacity = 1;
            WeatherIconA.Opacity = 0;
            _weatherIconFlip = true;
        }
        else
        {
            WeatherIconA.Child = path;
            WeatherIconA.Opacity = 1;
            WeatherIconB.Opacity = 0;
            _weatherIconFlip = false;
        }
        _lastWeatherIconKey = key;
    }

    private static string Fallback(OverlayKind kind) => kind switch
    {
        OverlayKind.Notification => "Уведомление",
        OverlayKind.Progress => "Прогресс",
        OverlayKind.Media => "Без названия",
        OverlayKind.Timer => "Таймер",
        OverlayKind.Error => "Ошибка",
        OverlayKind.Expanded => "Обзор",
        OverlayKind.Weather => "Погода",
        _ => ""
    };

    private static MenuItem Menu(string header, Action act)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => act();
        return item;
    }

    private void OnMediaPlay(object? sender, RoutedEventArgs e)
    {
        var next = OverlayMachine.Sanitize(_machine.Snapshot().Payload);
        next.Playing = !next.Playing;
        _machine.Dispatch(OverlayCommand.SetMedia, next);
        Paint();
    }
}
