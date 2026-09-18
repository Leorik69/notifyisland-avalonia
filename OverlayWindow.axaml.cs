using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NotifyIsland.Core;
using NotifyIsland.Demo;
using NotifyIsland.Integration.Windows;

namespace NotifyIsland;

public partial class OverlayWindow : Window
{
    private readonly OverlayMachine _machine = new();
    private readonly OverlayDispatcher _dispatch;
    private NamedPipeIpc? _ipc;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _demo = new() { Interval = TimeSpan.FromSeconds(3.2) };
    private readonly HwndMorph _hwnd;
    private bool _demoOn;
    private OverlayKind _lastKind = OverlayKind.Idle;
    private double _lastW = OverlayTokens.CollapsedW;
    private double _lastH = OverlayTokens.CollapsedH;
    private int _lastTimerSec = -1;
    private bool _hiding;
    private bool _hoverOpen;
    private string _iconKey = "";
    private int _clickPhase;
    private bool _freezeText;
    private string _frozenRow = "";
    private bool _volSync;

    public OverlayWindow()
    {
        InitializeComponent();
        _dispatch = new OverlayDispatcher(_machine);
        _hwnd = new HwndMorph(this);
        _hwnd.Completed += () =>
        {
            _freezeText = false;
            Paint();
            if (Program.MotionDebug) MotionHud.Text = HwndMorph.LastTiming;
        };
        IslandHost.Overlay = this;
        Opened += (_, _) =>
        {
            Win32Overlay.ApplyNoActivate(this);
            Position = OverlayPlacement.Compute(this, Width, Height);
            StartIpc();
            if (Program.DemoMode && !_demo.IsEnabled)
            {
                _demoOn = true;
                _demo.Start();
            }
        };
        KeyDown += OnKey;
        PrefsStore.Changed += OnPrefsChanged;
        Closed += (_, _) =>
        {
            PrefsStore.Changed -= OnPrefsChanged;
            _ipc?.Dispose();
        };
        _clock.Tick += (_, _) => TickClock();
        _tick.Tick += (_, _) =>
        {
            var before = _machine.Snapshot().Kind;
            _dispatch.Tick(200);
            Paint();
            MaybeCompleteSound(_machine.Snapshot());
            if (before != _machine.Snapshot().Kind) ApplySize();
        };
        _demo.Tick += (_, _) =>
        {
            try
            {
                DemoScript.Next(_machine);
                ApplySize();
                Paint();
            }
            catch (Exception ex)
            {
                IslandLog.Write("demo", ex.Message);
            }
        };
        _clock.Start();
        _tick.Start();
        ApplyTheme();
        MotionHud.IsVisible = Program.MotionDebug || Program.Diagnostics || PrefsStore.Current.Diagnostics;
        _machine.NotifyDurationMs = PrefsStore.Current.NotifyDurationMs;
        TickClock();
        ApplySize(false);
        Paint();
        if (Program.DemoMode) StartDemo();
        if (Program.OpenSettingsOnStart)
            Dispatcher.UIThread.Post(IslandHost.OpenSettings, DispatcherPriority.Background);
        if (PrefsStore.Current.ListenToasts)
            _ = ToastHub.RefreshAsync(true);
        WeatherHub.Changed += () => Dispatcher.UIThread.Post(() => { PaintWeather(); Paint(); });
        WeatherHub.Start();
        AppBadgeHub.Changed += () => Dispatcher.UIThread.Post(OnBadgeChanged);
        if (!PrefsStore.Current.OverlayVisible) Hide();
    }

    private void StartIpc()
    {
        try
        {
            _ipc = new NamedPipeIpc();
            _ipc.NotifyReceived += req => Dispatcher.UIThread.Post(() =>
            {
                _dispatch.Notify(req);
                ApplySize();
                Paint();
            });
            _ipc.DismissReceived += id => Dispatcher.UIThread.Post(() =>
            {
                _dispatch.Dismiss(id);
                ApplySize();
                Paint();
            });
            _ipc.Start();
            IslandLog.Write("ipc", _ipc.Status);
        }
        catch (Exception ex)
        {
            IslandLog.Write("ipc", "start failed " + ex.Message);
        }
    }

    public bool DemoRunning => _demoOn;

    public void ToggleDemo()
    {
        if (_demoOn) StopDemo();
        else StartDemo();
    }

    public void PreviewAnimation()
    {
        _machine.Dispatch(OverlayCommand.Notify, new OverlayPayload
        {
            Title = "Preview",
            Body = "Theme animation",
            DurationMs = NotifyTemplates.DurationMs(PrefsStore.Current, "notify")
        });
        ApplySize();
        Paint();
        IslandAnimator.FastInvoke(Pill);
    }

    public void ShowToast(string title, string body, string? template = null)
    {
        var tpl = NotifyTemplates.Normalize(template);
        _dispatch.Notify(new NotificationRequest
        {
            Id = NotificationId.Parse(tpl + ":" + title),
            Title = title,
            Body = body,
            Template = tpl,
            DurationMs = NotifyTemplates.DurationMs(PrefsStore.Current, tpl),
            Replace = true
        });
        ApplySize();
        Paint();
        _ = AppBadgeHub.RefreshAsync();
    }

    public async void SetVisibleAnimated(bool visible)
    {
        if (visible)
        {
            _hiding = false;
            Opacity = 0;
            Show();
            PrefsStore.Mutate(p => p.OverlayVisible = true);
            IslandAnimator.WireOpacity(this, Motion.FadeMs);
            Opacity = 1;
            IslandAnimator.FastInvoke(Pill);
            return;
        }
        if (_hiding) return;
        _hiding = true;
        IslandAnimator.WireOpacity(this, Motion.FadeMs);
        Opacity = 0;
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await Task.Delay(Motion.FadeMs + 20);
            if (!_hiding) return;
            Hide();
            Opacity = 1;
            PrefsStore.Mutate(p => p.OverlayVisible = false);
            _hiding = false;
        });
    }

    private void OnPrefsChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            ApplyTheme();
            _machine.NotifyDurationMs = PrefsStore.Current.NotifyDurationMs;
            ApplySize();
            Paint();
            if (PrefsStore.Current.ListenToasts) _ = ToastHub.RefreshAsync(true);
            _ = WeatherHub.RefreshAsync(false);
            _ = AppBadgeHub.RefreshAsync();
        });
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F9) { ToggleDemo(); e.Handled = true; }
        else if (e.Key == Key.Escape) { _machine.Dispatch(OverlayCommand.Collapse); ApplySize(); Paint(); e.Handled = true; }
        else if (e.Key == Key.Enter)
        {
            _dispatch.InvokeAction(NotificationAction.Content);
            ApplySize(); Paint(); e.Handled = true;
        }
        else if (e.Key == Key.C)
        {
            _dispatch.InvokeAction(NotificationAction.Cancel);
            ApplySize(); Paint(); e.Handled = true;
        }
    }

    private void StartDemo()
    {
        _demoOn = true; _demo.Start();
        AppBadgeHub.SetPreview("Telegram", 2);
        DemoScript.Reset();
        DemoScript.Next(_machine); ApplySize(); Paint();
    }

    private void StopDemo()
    {
        _demoOn = false; _demo.Stop();
        AppBadgeHub.ClearPreview();
        _ = AppBadgeHub.RefreshAsync();
        _machine.Dispatch(OverlayCommand.Clear); ApplySize(); Paint();
    }

    private void TickClock()
    {
        var fmt = PrefsStore.Current.ClockFormat;
        ClockText.Text = DateTime.Now.ToString(fmt, CultureInfo.InvariantCulture);
        if (_machine.Snapshot().Kind is OverlayKind.Idle or OverlayKind.Collapsed)
            ClockText.Opacity = 1;
    }

    private void ApplyTheme()
    {
        IslandAnimator.WireChrome(Pill);
        IslandAnimator.WireChrome(Glow);
        IslandAnimator.WireOpacity(ClockText, Motion.FadeMs);
        IslandAnimator.WireOpacity(OverlayPanel, Motion.FadeMs);
        IslandAnimator.WireOpacity(OverlayProgress, Motion.FadeMs);
        IslandAnimator.WireOpacity(OverlayTimer, Motion.FadeMs);
        IslandAnimator.WireOpacity(OverlayStack, Motion.FadeMs);
        IslandAnimator.WireOpacity(OverlayRow, Motion.FadeMs);
        IslandAnimator.WireOpacity(RowText, Motion.FadeMs);
        IslandAnimator.WireOpacity(RowProgress, Motion.FadeMs);
        IslandAnimator.WireOpacity(RowTimer, Motion.FadeMs);
        IslandAnimator.WireOpacity(MediaPlay, Motion.FadeMs);
        IslandAnimator.WireOpacity(KindIcon, Motion.FadeMs);
        IslandAnimator.WireOpacity(WeatherChip, Motion.FadeMs);
        IslandAnimator.WireProgress(OverlayProgress);
        IslandAnimator.WireProgress(RowProgress);
        IslandAnimator.WireOpacity(this, Motion.FadeMs);

        var pal = PaletteCatalog.Get(PrefsStore.Current.PaletteId);
        var accent = PaletteCatalog.AccentOf(PrefsStore.Current);
        var font = FontCatalog.Get(PrefsStore.Current.FontId);
        var family = new FontFamily(font.Family);
        var prefs = PrefsStore.Current;
        var opacity = Math.Clamp(prefs.Opacity * (1 - prefs.Glass * 0.35), 0.35, 1);
        var glowA = (byte)Math.Clamp(40 + prefs.GlowStrength * 140, 0, 255);
        Pill.Background = new SolidColorBrush(pal.Background, opacity);
        Pill.BorderBrush = new SolidColorBrush(pal.Border);
        var pad = prefs.Density == "compact" ? 8 : 12;
        CapsuleGrid.Margin = new Thickness(pad, 0);
        Pill.BorderThickness = new Thickness(prefs.BorderThickness);
        Pill.BoxShadow = new BoxShadows(new BoxShadow
        {
            Blur = 6 + prefs.GlowStrength * 22,
            Spread = 0,
            OffsetX = 0,
            OffsetY = 0,
            Color = Color.FromArgb(glowA, pal.Glow.R, pal.Glow.G, pal.Glow.B)
        });
        Glow.Background = new SolidColorBrush(pal.Glow, 0.12 + prefs.GlowStrength * 0.28);
        ClockText.Foreground = new SolidColorBrush(pal.Text);
        ClockText.FontFamily = family;
        OverlayTitle.FontFamily = family;
        OverlaySubtitle.FontFamily = family;
        OverlayStack.FontFamily = family;
        OverlayTimer.FontFamily = family;
        RowText.FontFamily = family;
        RowTimer.FontFamily = family;
        OverlayTitle.Foreground = new SolidColorBrush(pal.Text);
        OverlaySubtitle.Foreground = new SolidColorBrush(pal.TextSecondary);
        OverlayStack.Foreground = new SolidColorBrush(pal.TextSecondary);
        OverlayProgress.Foreground = new SolidColorBrush(accent);
        RowText.Foreground = new SolidColorBrush(pal.Text);
        RowTimer.Foreground = new SolidColorBrush(pal.Text);
        RowProgress.Foreground = new SolidColorBrush(accent);
        KindIcon.Fill = new SolidColorBrush(accent);
        KindGlyph.Foreground = new SolidColorBrush(accent);
        MediaPlayIcon.Fill = new SolidColorBrush(accent);
        MediaPlayGlyph.Foreground = new SolidColorBrush(accent);
        WeatherIcon.Fill = new SolidColorBrush(accent);
        WeatherGlyph.Foreground = new SolidColorBrush(accent);
        WeatherTemp.Foreground = new SolidColorBrush(pal.Text);
        WeatherTemp.FontFamily = family;
        BadgeCount.Foreground = new SolidColorBrush(pal.Text);
        BadgeFallback.Fill = new SolidColorBrush(accent);
        var gs = TypeScale.GlyphPx(prefs.IconScale);
        var ts = TypeScale.TextPx(prefs.TextScale);
        KindIcon.Width = KindIcon.Height = gs;
        KindGlyph.FontSize = gs;
        BadgeLogo.Width = BadgeLogo.Height = gs + 2;
        BadgeFallback.Width = BadgeFallback.Height = gs;
        WeatherIcon.Width = WeatherIcon.Height = gs - 1;
        WeatherGlyph.FontSize = gs;
        MediaPlayIcon.Width = MediaPlayIcon.Height = Math.Max(8, gs - 4);
        MediaNextIcon.Width = MediaNextIcon.Height = Math.Max(8, gs - 4);
        MediaMuteIcon.Width = MediaMuteIcon.Height = Math.Max(8, gs - 4);
        MediaMuteIcon.Data = IslandIcons.Geometry(IslandGlyph.Volume, prefs.IconStyle);
        MediaNextIcon.Data = IslandIcons.Geometry(IslandGlyph.SkipNext, prefs.IconStyle);
        MediaNextIcon.Fill = new SolidColorBrush(accent);
        MediaMuteIcon.Fill = new SolidColorBrush(accent);
        ClockText.FontSize = ts;
        RowText.FontSize = Math.Max(11, ts - 1);
        RowTimer.FontSize = Math.Max(11, ts - 1);
        OverlayTitle.FontSize = ts;
        OverlaySubtitle.FontSize = Math.Max(10, ts - 2);
        WeatherTemp.FontSize = Math.Max(10, ts - 2);
        BadgeCount.FontSize = Math.Max(10, ts - 2);
    }

    private void ApplySize(bool animate = true)
    {
        var snap = _machine.Snapshot();
        var prefs = PrefsStore.Current;
        var badgeOn = ShowBadgeNow();
        var toW = snap.Kind is OverlayKind.Idle or OverlayKind.Collapsed
            ? prefs.IdleWidth + (badgeOn ? 36 : 0)
            : Math.Clamp(snap.Width, prefs.MinWidth, prefs.MaxWidth);
        var toH = prefs.ExpandHeight && snap.Kind is not (OverlayKind.Idle or OverlayKind.Collapsed)
            ? snap.Height
            : prefs.IdleHeight;
        Pill.Height = toH;
        var radius = !prefs.ExpandHeight || prefs.CornerRadius <= 0 ? toH / 2 : prefs.CornerRadius;
        Pill.CornerRadius = new CornerRadius(radius);
        Glow.Height = toH + 8 + prefs.GlowStrength * 6;
        Glow.CornerRadius = new CornerRadius(radius + 4);
        var doAnim = animate && !prefs.ReduceMotion && prefs.Animation != "none";
        if (doAnim)
        {
            _freezeText = true;
            var collapse = toW + 4 < _lastW;
            if (collapse)
            {
                IslandAnimator.WireOpacity(OverlayRow, Motion.FadeMs);
                OverlayRow.Opacity = 0;
            }
            else
            {
                Pill.Width = Math.Max(_lastW, toW);
                Glow.Width = Pill.Width + 10 + prefs.GlowStrength * 8;
            }
            _hwnd.To(Pill, collapse ? OverlayRow : null, toW, toH, Motion.WidthMorphMs(toW - _lastW), collapse && prefs.ShowAppBadge);
        }
        else
        {
            Pill.Width = toW;
            Glow.Width = toW + 10 + prefs.GlowStrength * 8;
            Width = toW;
            Height = toH;
            Position = OverlayPlacement.Compute(this, toW, toH);
        }
        _lastW = toW;
        _lastH = toH;
        if (snap.Kind != _lastKind)
        {
            PlayMotion(snap.Kind);
            IslandSounds.CueForKind(snap.Kind, snap.Payload.Template);
            _lastKind = snap.Kind;
        }
        MaybeCompleteSound(snap);
    }

    private bool _completeArmed = true;

    private void MaybeCompleteSound(OverlaySnapshot snap)
    {
        if (snap.Kind == OverlayKind.Progress && snap.Payload.Progress >= 0.995)
        {
            if (_completeArmed)
            {
                _completeArmed = false;
                IslandSounds.Cue(IslandSound.Complete);
            }
            return;
        }
        if (snap.Kind == OverlayKind.Timer && snap.Payload.RemainingSeconds <= 0.05)
        {
            if (_completeArmed)
            {
                _completeArmed = false;
                IslandSounds.Cue(IslandSound.Complete);
            }
            return;
        }
        if (snap.Kind is OverlayKind.Progress or OverlayKind.Timer) _completeArmed = true;
    }

    private int _badgeCount = -1;
    private bool _badgeVis;

    private void OnBadgeChanged()
    {
        var vis = ShowBadgeNow();
        var n = AppBadgeHub.Current?.Count ?? 0;
        PaintBadge();
        Paint();
        if (n != _badgeCount && n > 0)
            IslandAnimator.TickFade(BadgeCount);
        _badgeCount = n;
        if (vis != _badgeVis)
        {
            _badgeVis = vis;
            ApplySize();
        }
    }

    private void PlayMotion(OverlayKind kind)
    {
        var tpl = NotifyTemplates.Normalize(_machine.Snapshot().Payload.Template);
        if (kind is OverlayKind.Notification or OverlayKind.Stack or OverlayKind.Error)
            IslandAnimator.SlideFromBadge(OverlayRow);
        if (kind == OverlayKind.Notification && tpl == NotifyTemplates.Chat)
            IslandAnimator.PopChat(KindIcon);
        else if (kind == OverlayKind.Notification && tpl == NotifyTemplates.Call)
            IslandAnimator.Breathe(Glow);
        else if (kind == OverlayKind.Error || tpl == NotifyTemplates.Warn)
            IslandAnimator.WarnFlash(RowText);
        else if (kind is OverlayKind.Notification or OverlayKind.Stack)
            IslandAnimator.FastInvoke(KindIcon);
        if (kind == OverlayKind.Progress || tpl == NotifyTemplates.Download)
            IslandAnimator.FastInvoke(RowProgress);
        if (kind == OverlayKind.Media)
            IslandAnimator.FastInvoke(PlayerChrome);
        if (PrefsStore.Current.Animation == "breathe" && kind is OverlayKind.Notification)
            IslandAnimator.Breathe(Glow);
    }

    private void PlaceTopCenter() => Position = OverlayPlacement.Compute(this, Width, Height);

    private void Paint()
    {
        var pal = PaletteCatalog.Get(PrefsStore.Current.PaletteId);
        var snap = _machine.Snapshot();
        var kind = snap.Kind; var p = snap.Payload;
        var overlayOn = kind is OverlayKind.Notification or OverlayKind.Progress or OverlayKind.Media
            or OverlayKind.Timer or OverlayKind.TimerComplete or OverlayKind.Error or OverlayKind.Expanded or OverlayKind.Stack;
        var widthOnly = !PrefsStore.Current.ExpandHeight;
        if (!(_freezeText && _hwnd.IsRunning))
        {
            OverlayRow.Opacity = overlayOn && widthOnly ? 1 : 0;
            OverlayRow.IsHitTestVisible = overlayOn && widthOnly;
            OverlayPanel.Opacity = overlayOn && !widthOnly ? 1 : 0;
            OverlayPanel.IsHitTestVisible = overlayOn && !widthOnly;
            ClockText.Opacity = overlayOn ? 0 : 1;
        }
        var title = string.IsNullOrWhiteSpace(p.Title) ? Fallback(kind) : p.Title;
        var sub = string.IsNullOrWhiteSpace(p.Subtitle) ? p.Body : p.Subtitle;
        OverlayTitle.Text = title;
        OverlaySubtitle.Text = sub;
        OverlayStack.Text = p.Line2;
        OverlayStack.Opacity = kind == OverlayKind.Stack && !string.IsNullOrWhiteSpace(p.Line2) ? 1 : 0;
        var row = title;
        if (!string.IsNullOrWhiteSpace(sub)) row += " · " + sub;
        if (kind == OverlayKind.Progress)
        {
            var pct = (int)Math.Round(p.Progress * 100);
            row += " " + pct + "%";
            if (p.EtaSeconds > 0.5) row += " · " + TimeSpan.FromSeconds(p.EtaSeconds).ToString(@"m\:ss");
        }
        if (kind == OverlayKind.TimerComplete) row = (string.IsNullOrWhiteSpace(title) ? "00:00" : title) + " · done";
        if (_freezeText && _hwnd.IsRunning && !string.IsNullOrEmpty(_frozenRow))
            RowText.Text = _frozenRow;
        else
        {
            RowText.Text = row;
            _frozenRow = row;
        }
        OverlayProgress.Opacity = !widthOnly && kind is OverlayKind.Progress or OverlayKind.Media ? 1 : 0;
        OverlayProgress.Value = p.Progress * 100;
        RowProgress.Opacity = widthOnly && kind is OverlayKind.Progress or OverlayKind.Media ? 1 : 0;
        RowProgress.Value = p.Progress * 100;
        OverlayTimer.Opacity = !widthOnly && kind == OverlayKind.Timer ? 1 : 0;
        RowTimer.Opacity = widthOnly && kind == OverlayKind.Timer ? 1 : 0;
        var sec = (int)Math.Ceiling(p.RemainingSeconds);
        var clock = kind == OverlayKind.Timer ? TimeSpan.FromSeconds(sec).ToString(@"mm\:ss") : "";
        OverlayTimer.Text = clock;
        RowTimer.Text = clock;
        if (kind == OverlayKind.Timer && sec != _lastTimerSec)
        {
            _lastTimerSec = sec;
            IslandAnimator.TickFade(widthOnly ? RowTimer : OverlayTimer);
        }
        var warn = NotifyTemplates.Normalize(p.Template) == NotifyTemplates.Warn
            || p.Urgency == NotifyIsland.Core.NotifyUrgency.Warning;
        var ok = p.Urgency == NotifyIsland.Core.NotifyUrgency.Success || kind == OverlayKind.TimerComplete;
        var err = kind == OverlayKind.Error || p.Urgency == NotifyIsland.Core.NotifyUrgency.Error;
        var ink = err ? pal.Error : warn ? Color.Parse("#E6C35C") : ok ? Color.Parse("#8FDB8F") : pal.Text;
        OverlayTitle.Foreground = new SolidColorBrush(ink);
        RowText.Foreground = new SolidColorBrush(ink);
        var canCancel = kind == OverlayKind.Progress && p.Cancellable;
        CancelProgress.Opacity = canCancel && widthOnly ? 1 : 0;
        CancelProgress.IsHitTestVisible = canCancel && widthOnly;
        if (Program.Diagnostics || PrefsStore.Current.Diagnostics)
        {
            MotionHud.IsVisible = true;
            MotionHud.Text = QuietHours.Label + " · dpi " + OverlayPlacement.LastScale.ToString("0.##") + " · toast " + ToastHub.Status;
        }
        var rtl = LooksRtl(title + sub);
        OverlayRow.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        var player = kind == OverlayKind.Media;
        MediaPlay.Opacity = player ? 1 : 0;
        MediaPlay.IsHitTestVisible = player;
        MediaNext.Opacity = player ? 1 : 0;
        MediaNext.IsHitTestVisible = player;
        MediaMute.Opacity = player ? 1 : 0;
        MediaMute.IsHitTestVisible = player;
        MasterVol.Opacity = player ? 1 : 0;
        MasterVol.IsHitTestVisible = player;
        if (player) SyncMasterVolUi();
        var glyph = IslandIcons.ForPayload(kind, p);
        var style = IslandIcons.Normalize(PrefsStore.Current.IconStyle);
        var key = style + ":" + glyph + ":" + overlayOn;
        if (style == "mdl2")
        {
            KindIcon.Opacity = 0;
            KindGlyph.IsVisible = overlayOn;
            KindGlyph.Text = IslandIcons.Mdl2(glyph);
            MediaPlayIcon.Opacity = 0;
            MediaPlayGlyph.IsVisible = true;
            MediaPlayGlyph.Text = IslandIcons.Mdl2(p.Playing ? IslandGlyph.MediaPause : IslandGlyph.MediaPlay);
        }
        else
        {
            KindGlyph.IsVisible = false;
            MediaPlayGlyph.IsVisible = false;
            MediaPlayIcon.Opacity = 1;
            MediaPlayIcon.Data = IslandIcons.Geometry(p.Playing ? IslandGlyph.MediaPause : IslandGlyph.MediaPlay, style);
            if (key != _iconKey)
            {
                _iconKey = key;
                if (overlayOn)
                    IslandAnimator.CrossfadeIcon(KindIcon, () => KindIcon.Data = IslandIcons.Geometry(glyph, style));
                else
                    KindIcon.Data = IslandIcons.Geometry(glyph, style);
            }
        }
        PaintWeather();
        PaintBadge();
        ToolTip.SetTip(this, kind == OverlayKind.Idle ? "NotifyIsland" : OverlayTitle.Text);
    }

    private bool ShowBadgeNow()
    {
        var prefs = PrefsStore.Current;
        if (!prefs.ShowAppBadge) return false;
        var b = AppBadgeHub.Current;
        return b is { Count: > 0 } && (_demoOn && b.Preview || !_demoOn && !b.Preview && ToastHub.Allowed);
    }

    private void PaintBadge()
    {
        var idle = _machine.Snapshot().Kind is OverlayKind.Idle or OverlayKind.Collapsed;
        var show = idle && ShowBadgeNow() && !_hwnd.IsRunning;
        AppBadge.Opacity = show ? 1 : 0;
        AppBadge.IsHitTestVisible = show;
        if (!show) return;
        var b = AppBadgeHub.Current!;
        var style = PrefsStore.Current.BadgeStyle;
        var n = b.Count > 9 ? "9+" : b.Count.ToString();
        BadgeCount.Text = style == "dot" ? "●" : n;
        BadgeCount.IsVisible = style != "icon-count" || true;
        if (style == "count")
        {
            BadgeLogo.IsVisible = false;
            BadgeFallback.IsVisible = false;
            return;
        }
        if (style == "dot")
        {
            BadgeLogo.IsVisible = false;
            BadgeFallback.IsVisible = false;
            return;
        }
        if (b.Logo is not null)
        {
            BadgeLogo.Source = b.Logo;
            BadgeLogo.IsVisible = true;
            BadgeFallback.IsVisible = false;
        }
        else
        {
            BadgeLogo.IsVisible = false;
            BadgeFallback.IsVisible = true;
            BadgeFallback.Data = IslandIcons.Geometry(IslandGlyph.Chat, PrefsStore.Current.IconStyle);
        }
    }

    private void PaintWeather()
    {
        var idle = _machine.Snapshot().Kind is OverlayKind.Idle or OverlayKind.Collapsed;
        var wx = WeatherHub.Current;
        var pos = PrefsStore.Current.WeatherPosition;
        var show = idle && PrefsStore.Current.ShowWeather && pos != "hide" && pos != "expand" && wx is { Ok: true } && !_hwnd.IsRunning;
        WeatherChip.Opacity = show ? 1 : 0;
        if (!show || wx is null) return;
        WeatherTemp.Text = WeatherHub.TempLabel(wx);
        var style = IslandIcons.Normalize(PrefsStore.Current.IconStyle);
        var g = WeatherHub.GlyphFor(wx.WeatherCode);
        if (style == "mdl2")
        {
            WeatherIcon.Opacity = 0;
            WeatherGlyph.IsVisible = true;
            WeatherGlyph.Text = IslandIcons.Mdl2Weather(g);
        }
        else
        {
            WeatherGlyph.IsVisible = false;
            WeatherIcon.Opacity = 1;
            WeatherIcon.Data = IslandIcons.WeatherGeometry(g, style);
        }
    }

    private OverlayPayload ExpandPayload()
    {
        var wx = WeatherHub.Current;
        if (wx is { Ok: true })
        {
            var city = string.IsNullOrWhiteSpace(wx.City) ? wx.Condition : wx.City;
            return new OverlayPayload
            {
                Title = city,
                Body = wx.Condition + " · " + WeatherHub.TempLabel(wx)
            };
        }
        return new OverlayPayload
        {
            Title = DateTime.Now.ToString("dddd", CultureInfo.CurrentCulture),
            Body = DateTime.Now.ToString("d MMM", CultureInfo.CurrentCulture)
        };
    }

    private static string Fallback(OverlayKind kind) => kind switch
    {
        OverlayKind.Notification => "Notification",
        OverlayKind.Stack => "Queue",
        OverlayKind.Progress => "Progress",
        OverlayKind.Media => "Untitled",
        OverlayKind.Timer => "Timer",
        OverlayKind.Error => "Error",
        OverlayKind.Expanded => "Overview",
        _ => ""
    };

    private void OnPillPressed(object? sender, PointerPressedEventArgs e)
    {
        if (HitsMedia(e)) return;
        var pt = e.GetCurrentPoint(this);
        if (pt.Properties.IsLeftButtonPressed)
        {
            var mods = e.KeyModifiers;
            var shift = mods.HasFlag(KeyModifiers.Shift) || mods.HasFlag(KeyModifiers.Alt) || mods.HasFlag(KeyModifiers.Control);
            if (!shift && TryHandleClickCycle())
            {
                e.Handled = true;
                return;
            }
            var kind = _machine.Snapshot().Kind;
            if (kind is OverlayKind.Idle or OverlayKind.Collapsed)
            {
                _hoverOpen = false;
                _machine.Dispatch(OverlayCommand.Expand, ExpandPayload());
                ApplySize(); Paint();
            }
            else if (kind == OverlayKind.Expanded)
            {
                _hoverOpen = false;
                _machine.Dispatch(OverlayCommand.Collapse); ApplySize(); Paint();
            }
            else if (kind == OverlayKind.Stack)
            {
                var cur = _machine.Snapshot().Payload;
                if (!string.IsNullOrWhiteSpace(cur.Line2))
                    _machine.Dispatch(OverlayCommand.Notify, new OverlayPayload { Title = cur.Line2 });
                else
                    _machine.Dispatch(OverlayCommand.Clear);
                ApplySize(); Paint();
            }
            else if (kind is OverlayKind.Notification or OverlayKind.Error or OverlayKind.TimerComplete)
            {
                _dispatch.InvokeAction(NotificationAction.Content);
                _machine.Dispatch(OverlayCommand.Clear); ApplySize(); Paint();
            }
            e.Handled = true;
            return;
        }
        if (!pt.Properties.IsRightButtonPressed) return;
        var menu = new ContextMenu();
        menu.Items.Add(Menu("Settings", IslandHost.OpenSettings));
        menu.Items.Add(Menu(_demoOn ? "Stop demo" : "Demo F9", ToggleDemo));
        menu.Items.Add(Menu("Hide overlay", () => IslandHost.ToggleOverlay()));
        menu.Items.Add(Menu("Exit", IslandHost.Exit));
        menu.Open(Pill);
        e.Handled = true;
    }

    private bool HitsMedia(PointerEventArgs e)
    {
        for (var v = e.Source as Visual; v is not null; v = v.GetVisualParent())
            if (ReferenceEquals(v, MediaPlay) || ReferenceEquals(v, MediaNext) ||
                ReferenceEquals(v, MediaMute) || ReferenceEquals(v, MasterVol) ||
                ReferenceEquals(v, PlayerChrome))
                return true;
        return false;
    }

    private bool TryHandleClickCycle()
    {
        var mode = PrefsStore.Current.ClickMode;
        if (mode is "off" or "expand") return false;
        string[] steps = mode switch
        {
            "player" => new[] { "player" },
            "center" => new[] { "center" },
            "cycle-player-center" => new[] { "player", "center" },
            _ => new[] { "center", "player" }
        };
        var step = steps[_clickPhase % steps.Length];
        _clickPhase++;
        if (step == "center")
        {
            if (_machine.Snapshot().Kind == OverlayKind.Media)
            {
                _machine.Dispatch(OverlayCommand.Collapse);
                ApplySize(); Paint();
            }
            ActionCenter.TryOpen();
            return true;
        }
        _ = ShowPlayerAsync();
        return true;
    }

    private async System.Threading.Tasks.Task ShowPlayerAsync()
    {
        var smtc = await SmtcHub.RefreshAsync();
        _machine.Dispatch(OverlayCommand.SetMedia, new OverlayPayload
        {
            Title = smtc.HasSession ? smtc.Title : "No media",
            Subtitle = smtc.HasSession ? smtc.Artist : SmtcHub.Status,
            Playing = smtc.Playing,
            Template = "media"
        });
        ApplySize();
        Paint();
    }

    private void SyncMasterVolUi()
    {
        if (!SystemVolume.TryGet(out var s, out var mute)) return;
        _volSync = true;
        MasterVol.Value = s;
        MediaMuteIcon.Data = IslandIcons.Geometry(mute ? IslandGlyph.Mute : IslandGlyph.Volume, PrefsStore.Current.IconStyle);
        _volSync = false;
    }

    private void OnMasterVol(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_volSync || e.Property != Slider.ValueProperty) return;
        SystemVolume.TrySet((float)MasterVol.Value);
    }

    private void OnMediaMute(object? sender, RoutedEventArgs e)
    {
        if (SystemVolume.TryGet(out _, out var mute))
            SystemVolume.TrySetMute(!mute);
        SyncMasterVolUi();
        IslandHost.Settings?.SyncMasterVolume();
    }

    private async void OnMediaNext(object? sender, RoutedEventArgs e)
    {
        await SmtcHub.NextAsync();
        await ShowPlayerAsync();
    }

    private void OnPillEntered(object? sender, PointerEventArgs e)
    {
        if (!PrefsStore.Current.HoverPeek || _demoOn) return;
        var kind = _machine.Snapshot().Kind;
        if (kind is not (OverlayKind.Idle or OverlayKind.Collapsed)) return;
        _hoverOpen = true;
        _machine.Dispatch(OverlayCommand.Expand, ExpandPayload());
        ApplySize(); Paint();
    }

    private void OnPillExited(object? sender, PointerEventArgs e)
    {
        if (!_hoverOpen) return;
        _hoverOpen = false;
        _machine.Dispatch(OverlayCommand.Collapse);
        ApplySize(); Paint();
    }

    private void OnCancelProgress(object? sender, RoutedEventArgs e)
    {
        _dispatch.InvokeAction(NotificationAction.Cancel);
        _machine.Dispatch(OverlayCommand.Clear);
        ApplySize();
        Paint();
    }

    private static bool LooksRtl(string s)
    {
        foreach (var c in s)
            if (c is >= '\u0590' and <= '\u08FF') return true;
        return false;
    }

    private static MenuItem Menu(string header, Action act)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => act();
        return item;
    }

    private async void OnMediaPlay(object? sender, RoutedEventArgs e)
    {
        await SmtcHub.TogglePlayAsync();
        var smtc = SmtcHub.Current;
        if (smtc.HasSession)
        {
            _machine.Dispatch(OverlayCommand.SetMedia, new OverlayPayload
            {
                Title = smtc.Title, Subtitle = smtc.Artist, Playing = smtc.Playing, Template = "media"
            });
        }
        else
        {
            var next = OverlayMachine.Sanitize(_machine.Snapshot().Payload);
            next.Playing = !next.Playing;
            _machine.Dispatch(OverlayCommand.SetMedia, next);
        }
        Paint();
    }
}
