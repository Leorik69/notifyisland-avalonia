using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
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

    // Swipe tracking
    private Point _pressOrigin;
    private bool _pressing;
    private bool _weatherIconFlip;
    private string _lastWeatherIconKey = "";
    private readonly TranslateTransform _pillTranslate = new();

    public OverlayWindow()
    {
        InitializeComponent();
        Pill.RenderTransform = _pillTranslate;
        _settings = AppSettings.Load();
        _machine.WeatherEnabled = _settings.WeatherEnabled;
        _weather = new WindowsWeatherSource(_settings.Latitude, _settings.Longitude);

        EnableMorphTransitions();
        WirePointerGestures();
        SeedIcons();

        Opened += (_, _) =>
        {
            Win32Overlay.ApplyNoActivate(this);
            PlaceTopCenter();
            _ = RefreshWeatherAsync();
        };
        KeyDown += OnKey;
        _clock.Tick += (_, _) => TickClock();
        _tick.Tick += (_, _) =>
        {
            var before = _machine.Snapshot().Kind;
            _machine.Tick(200);
            Paint();
            if (before != _machine.Snapshot().Kind) ApplySize();
        };
        _demo.Tick += (_, _) => { _machine.Dispatch(OverlayCommand.DemoNext); ApplySize(); Paint(); };
        _weatherTimer.Tick += (_, _) => _ = RefreshWeatherAsync();

        _clock.Start();
        _tick.Start();
        _weatherTimer.Start();
        TickClock();
        ApplySize();
        Paint();
        if (Program.DemoMode) StartDemo();
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
        };
        Pill.Transitions = new Transitions
        {
            new DoubleTransition { Property = Border.WidthProperty, Duration = duration, Easing = softOut },
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
            Pill.Background = new SolidColorBrush(Color.Parse("#121214"));
        };
        Pill.PointerExited += (_, _) =>
        {
            Pill.BorderBrush = new SolidColorBrush(Color.Parse("#28FFFFFF"));
            Pill.Background = new SolidColorBrush(Color.Parse("#080808"));
        };
    }

    private void WirePointerGestures()
    {
        Pill.PointerPressed += OnPillPointerPressed;
        Pill.PointerMoved += OnPillPointerMoved;
        Pill.PointerReleased += OnPillPointerReleased;
        Pill.PointerCaptureLost += (_, _) => ResetSwipeVisual();
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
        _pressing = true;
        e.Pointer.Capture(Pill);
        e.Handled = true;
    }

    private void OnPillPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_pressing) return;
        var pos = e.GetPosition(this);
        var dx = pos.X - _pressOrigin.X;
        var dy = pos.Y - _pressOrigin.Y;
        var dist = Math.Sqrt(dx * dx + dy * dy);
        // Rubber-band preview (clamped)
        var damp = 0.45;
        _pillTranslate.X = Math.Clamp(dx * damp, -56, 56);
        _pillTranslate.Y = Math.Clamp(dy * damp, -40, 40);
    }

    private void OnPillPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_pressing) return;
        _pressing = false;
        e.Pointer.Capture(null);

        var pos = e.GetPosition(this);
        var dx = pos.X - _pressOrigin.X;
        var dy = pos.Y - _pressOrigin.Y;
        var adx = Math.Abs(dx);
        var ady = Math.Abs(dy);
        var dist = Math.Sqrt(dx * dx + dy * dy);

        ResetSwipeVisual();

        if (dist <= OverlayTokens.SwipeClickMaxPx)
        {
            // Short click → Action Center when idle/collapsed
            var kind = _machine.Snapshot().Kind;
            if (kind is OverlayKind.Idle or OverlayKind.Collapsed)
                OpenActionCenter();
            e.Handled = true;
            return;
        }

        if (dist < OverlayTokens.SwipeFirePx)
        {
            // Under threshold — rubber-band already snapped via ResetSwipeVisual
            e.Handled = true;
            return;
        }

        if (adx >= ady)
        {
            // Horizontal: left = next, right = prev
            _machine.Dispatch(dx < 0 ? OverlayCommand.CycleNext : OverlayCommand.CyclePrev);
            ApplySize();
            Paint();
        }
        else
        {
            if (dy > 0)
            {
                _machine.Dispatch(OverlayCommand.Collapse);
                ApplySize();
                Paint();
            }
            else
            {
                _machine.Dispatch(OverlayCommand.ExpandWidget);
                ApplySize();
                Paint();
            }
        }

        e.Handled = true;
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
        menu.Items.Add(Menu("Свернуть", () => { _machine.Dispatch(OverlayCommand.Collapse); ApplySize(); Paint(); }));
        menu.Items.Add(Menu("Выход", () => (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown()));
        menu.Open(Pill);
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
        if (_settings.WeatherEnabled)
            _ = RefreshWeatherAsync();
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
        else if (e.Key == Key.Escape) { _machine.Dispatch(OverlayCommand.Collapse); ApplySize(); Paint(); e.Handled = true; }
    }

    private void StartDemo()
    {
        _demoOn = true;
        _machine.Dispatch(OverlayCommand.Clear);
        _machine.Dispatch(OverlayCommand.Notify, new OverlayPayload { Title = "Сообщение", Body = "Демо уведомление" });
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
        var h = OverlayTokens.CollapsedH;
        Width = snap.Width;
        Height = h;
        Pill.Width = snap.Width;
        Pill.Height = h;
        Pill.CornerRadius = new CornerRadius(h / 2);
        PlaceTopCenter();
    }

    private void PlaceTopCenter()
    {
        var screen = Screens.Primary ?? Screens.ScreenFromWindow(this);
        if (screen is null) return;
        var wa = screen.WorkingArea;
        var scale = RenderScaling;
        var pw = (int)Math.Round(Width * scale);
        var ph = (int)Math.Round(Height * scale);
        var maxX = Math.Max(wa.X, wa.X + wa.Width - pw);
        var maxY = Math.Max(wa.Y, wa.Y + wa.Height - ph);
        var x = Math.Clamp(wa.X + (wa.Width - pw) / 2, wa.X, maxX);
        var y = Math.Clamp(wa.Y + 8, wa.Y, maxY);
        Position = new PixelPoint(x, y);
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

        // Minimal weather on idle/collapsed
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

        // Crossfade A ↔ B
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

    private static void OpenActionCenter()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-actioncenter:",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Warn("OpenActionCenter failed", ex);
        }
    }

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
