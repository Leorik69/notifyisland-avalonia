using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NotifyIsland.Core;

namespace NotifyIsland;

public partial class SettingsWindow : Window
{
    private bool _boot;
    private bool _syncing;

    private readonly DispatcherTimer _commitDelay = new() { Interval = TimeSpan.FromMilliseconds(180) };

    public SettingsWindow()
    {
        InitializeComponent();
        _commitDelay.Tick += (_, _) =>
        {
            _commitDelay.Stop();
            if (_boot) Commit();
        };
        Opened += (_, _) => Win32Overlay.ApplyNormalChrome(this);
        Ui.Apply(PrefsStore.Current.UiLanguage);
        PaletteBox.ItemsSource = Array.ConvertAll(PaletteCatalog.All, p => p.Name);
        FontBox.ItemsSource = Array.ConvertAll(FontCatalog.All, f => f.Name);
        IconBox.ItemsSource = new[] { "Fluent outline", "Fluent filled", "Segoe MDL2", "Weather soft", "Fluent color" };
        AnimBox.ItemsSource = new[] { "Morph + invoke", "Invoke", "Breathe", "Morph only" };
        ExpandSpeedBox.ItemsSource = new[] { "Fast 167", "Normal 250", "Slow 333" };
        CollapseSpeedBox.ItemsSource = new[] { "Fast 250", "Normal 333", "Slow 400" };
        LangBox.ItemsSource = new[] { "Русский", "English", "System / Система" };
        ClockBox.ItemsSource = new[] { "HH:mm", "HH:mm:ss", "h:mm tt" };
        DensityBox.ItemsSource = new[] { "Comfort", "Compact" };
        BadgeStyleBox.ItemsSource = new[] { "Icon + count", "Count only", "Dot" };
        TextSizeBox.ItemsSource = new[] { "Small", "Medium", "Large" };
        IconSizeBox.ItemsSource = new[] { "Small", "Medium", "Large" };
        ClickBox.ItemsSource = new[] { "Center then player/volume", "Player/volume then center", "Notification Center only", "Player/volume only", "Off (expand)" };
        WeatherPosBox.ItemsSource = new[] { "Right of clock", "Hide", "Expand only" };
        AnchorHBox.ItemsSource = new[] { "Left", "Center", "Right" };
        AnchorVBox.ItemsSource = new[] { "Top", "Center", "Bottom" };
        LayerBox.ItemsSource = new[] { "Always on top", "Normal window", "Desktop (HWND_BOTTOM)" };
        RenderModeBox.ItemsSource = new[] { "fixedHost", "resizeHost" };
        LoadFromPrefs();
        WeatherHub.Changed += OnWeatherStatus;
        Closed += (_, _) =>
        {
            WeatherHub.Changed -= OnWeatherStatus;
            if (_commitDelay.IsEnabled)
            {
                _commitDelay.Stop();
                Commit();
            }
        };
        IslandAnimator.WireChrome(PreviewPill);
        ApplyLang();
        _boot = true;
        ApplyNumericChrome();
        PaintPreview();
        PathHint.Text = Ui.T("saved") + PrefsStore.ActivePath;
        if (!string.IsNullOrEmpty(Program.SettingsShotPath))
        {
            WhatsNewBox.IsVisible = false;
            try { File.WriteAllText(Program.SettingsShotPath + ".log.txt", "ctor " + Program.SettingsShotPath); } catch { }
            Opened += (_, _) => DispatcherTimer.RunOnce(CaptureSettingsShot, TimeSpan.FromMilliseconds(900));
        }
    }

    private void ApplyNumericChrome()
    {
        var large = TextSizeBox.SelectedIndex == 2 || !string.IsNullOrEmpty(Program.SettingsShotPath);
        var w = large ? 200.0 : 172.0;
        var unit = large ? 56.0 : 48.0;
        var fs = large ? 16.0 : 14.0;
        foreach (var g in this.GetLogicalDescendants().OfType<Grid>())
        {
            if (g.ColumnDefinitions.Count != 3) continue;
            if (g.ColumnDefinitions[1].Width.IsStar) continue;
            g.ColumnDefinitions[1].Width = new GridLength(w);
            g.ColumnDefinitions[2].Width = new GridLength(unit);
        }
        foreach (var n in this.GetLogicalDescendants().OfType<NumericUpDown>())
        {
            n.MinWidth = w;
            n.MinHeight = large ? 42 : 36;
            n.FontSize = fs;
            n.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        }
        foreach (var u in this.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("unit")))
            u.FontSize = large ? 15 : 13;
        if (large)
        {
            MinWidth = 620;
            Width = Math.Max(Width, 680);
        }
    }

    private int _shotTries;

    private void CaptureSettingsShot()
    {
        var path = Program.SettingsShotPath;
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            File.AppendAllText(path + ".log.txt", "\ntry " + _shotTries + " bounds=" + Bounds);
            AppearanceExp.IsExpanded = true;
            OpacityNum.BringIntoView();
            GlassNum.BringIntoView();
            UpdateLayout();
            if (_shotTries++ < 2)
            {
                DispatcherTimer.RunOnce(CaptureSettingsShot, TimeSpan.FromMilliseconds(280));
                return;
            }
            var w = Math.Max(640, (int)Math.Ceiling(Bounds.Width));
            var h = Math.Max(400, (int)Math.Min(Math.Ceiling(Bounds.Height), 920));
            using var bmp = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
            bmp.Render(this);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            bmp.Save(path);
            File.AppendAllText(path + ".log.txt", "\nsaved " + new FileInfo(path).Length);
        }
        catch (Exception ex)
        {
            File.WriteAllText(path + ".err.txt", ex.ToString());
        }
        Environment.Exit(0);
    }

    private void Pair(Slider s, NumericUpDown n, double v)
    {
        _syncing = true;
        s.Value = v;
        n.Value = (decimal)v;
        _syncing = false;
    }

    private void LoadFromPrefs()
    {
        var p = PrefsStore.Current;
        PaletteBox.SelectedIndex = IndexOf(PaletteCatalog.All, x => x.Id == p.PaletteId);
        FontBox.SelectedIndex = IndexOf(FontCatalog.All, x => x.Id == p.FontId);
        IconBox.SelectedIndex = p.IconStyle switch
        {
            "fluent-fill" => 1,
            "mdl2" => 2,
            "weather-soft" => 3,
            "fluent-color" => 4,
            _ => 0
        };
        AnimBox.SelectedIndex = p.Animation switch { "pulse" => 1, "breathe" => 2, "none" => 3, _ => 0 };
        LangBox.SelectedIndex = p.UiLanguage switch { "en" => 1, "system" => 2, _ => 0 };
        Pair(ExpandMsSlider, ExpandMsNum, p.ExpandMs);
        Pair(CollapseMsSlider, CollapseMsNum, p.CollapseMs);
        ExpandSpeedBox.SelectedIndex = p.ExpandMs <= 180 ? 0 : p.ExpandMs >= 300 ? 2 : 1;
        CollapseSpeedBox.SelectedIndex = p.CollapseMs <= 270 ? 0 : p.CollapseMs >= 370 ? 2 : 1;
        ClockBox.SelectedIndex = p.ClockFormat switch { "HH:mm:ss" => 1, "h:mm tt" => 2, _ => 0 };
        DensityBox.SelectedIndex = p.Density == "compact" ? 1 : 0;
        BadgeStyleBox.SelectedIndex = p.BadgeStyle switch { "count" => 1, "dot" => 2, _ => 0 };
        TextSizeBox.SelectedIndex = p.TextScale switch { "small" => 0, "large" => 2, _ => 1 };
        IconSizeBox.SelectedIndex = p.IconScale switch { "small" => 0, "large" => 2, _ => 1 };
        ClickBox.SelectedIndex = p.ClickMode switch
        {
            "cycle-player-center" => 1,
            "center" => 2,
            "player" => 3,
            "off" => 4,
            _ => 0
        };
        WeatherPosBox.SelectedIndex = p.WeatherPosition switch { "hide" => 1, "expand" => 2, _ => 0 };
        AccentBox.Text = p.AccentHex;
        Pair(OpacitySlider, OpacityNum, p.Opacity);
        Pair(GlassSlider, GlassNum, p.Glass);
        Pair(BorderSlider, BorderNum, p.BorderThickness);
        Pair(GlowSlider, GlowNum, p.GlowStrength);
        Pair(RadiusSlider, RadiusNum, p.CornerRadius);
        Pair(WidthSlider, WidthNum, p.IdleWidth);
        Pair(HeightSlider, HeightNum, p.IdleHeight);
        ExpandHeightBox.IsChecked = p.ExpandHeight;
        Pair(MinWSlider, MinWNum, p.MinWidth);
        Pair(MaxWSlider, MaxWNum, p.MaxWidth);
        BadgeBox.IsChecked = p.ShowAppBadge;
        AnchorHBox.SelectedIndex = p.AnchorH switch { "left" => 0, "right" => 2, _ => 1 };
        AnchorVBox.SelectedIndex = p.AnchorV switch { "center" => 1, "bottom" => 2, _ => 0 };
        Pair(OffXSlider, OffXNum, p.OffsetX);
        Pair(OffYSlider, OffYNum, p.OffsetY);
        LayerBox.SelectedIndex = p.Layer switch { "normal" => 1, "desktop" => 2, _ => 0 };
        FillScreens();
        ScreenBox.SelectedIndex = Math.Clamp(p.ScreenIndex + 1, 0, Math.Max(0, itemsCount(ScreenBox) - 1));
        QuietFocusBox.IsChecked = p.SuppressFocusAssist;
        QuietFullBox.IsChecked = p.SuppressFullscreen;
        ReduceMotionBox.IsChecked = p.ReduceMotion;
        DiagBox.IsChecked = p.Diagnostics;
        RenderModeBox.SelectedIndex = p.RenderMode == "resizeHost" ? 1 : 0;
        WhatsNewBox.IsVisible = p.LastSeenVersion != "1.3.1";
        Pair(NotifySlider, NotifyNum, p.NotifyDurationMs);
        Pair(ChatSlider, ChatNum, p.ChatDurationMs);
        Pair(CallSlider, CallNum, p.CallDurationMs);
        HoverBox.IsChecked = p.HoverPeek;
        ActionCenterBox.IsChecked = p.ClickOpensActionCenter;
        AutoBox.IsChecked = p.AutoStart || AutoStart.IsEnabled();
        ToastBox.IsChecked = p.ListenToasts;
        ToastStatus.Text = "Toasts: " + ToastHub.Status;
        WeatherBox.IsChecked = p.ShowWeather;
        LocationBox.IsChecked = p.UseWindowsLocation;
        CityBox.Text = p.WeatherCity;
        Pair(WeatherMinSlider, WeatherMinNum, p.WeatherIntervalMin);
        WeatherStatus.Text = "Weather: " + WeatherHub.Status;
        SoundBox.IsChecked = p.SoundsEnabled;
        SoundNotifyBox.IsChecked = p.SoundNotify;
        SoundChatBox.IsChecked = p.SoundChat;
        SoundErrorBox.IsChecked = p.SoundError;
        SoundCompleteBox.IsChecked = p.SoundComplete;
        Pair(VolumeSlider, VolumeNum, p.SoundVolume);
        BadgeAppsBox.Text = p.BadgeApps;
        SyncMasterVolume();
        ToastStatus.Text = PrefsStore.Current.ListenToasts
            ? "Toasts: " + ToastHub.Status + " — " + ToastHub.Detail
            : "Toasts: Off";
    }

    private void FillScreens()
    {
        var items = new System.Collections.Generic.List<string> { "Primary (auto)" };
        if (IslandHost.Overlay?.Screens is { } screens)
        {
            var i = 0;
            foreach (var s in screens.All)
            {
                items.Add("Display " + i + " · " + s.Bounds.Width + "x" + s.Bounds.Height + " @" + s.Scaling.ToString("0.##") + "x");
                i++;
            }
        }
        ScreenBox.ItemsSource = items;
    }

    private void OnWeatherStatus() => Dispatcher.UIThread.Post(() => WeatherStatus.Text = "Weather: " + WeatherHub.Status);

    private void OnChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_boot) return;
        ApplyNumericChrome();
        Commit();
        IslandAnimator.FastInvoke(PreviewPill);
    }

    private void OnSlider(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!_boot || _syncing || e.Property != Slider.ValueProperty) return;
        if (sender is Slider s) SyncFromSlider(s);
        _commitDelay.Stop();
        _commitDelay.Start();
    }

    private void OnNum(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (!_boot || _syncing) return;
        if (sender is NumericUpDown n) SyncFromNum(n);
        _commitDelay.Stop();
        _commitDelay.Start();
    }

    private void SyncFromSlider(Slider s)
    {
        _syncing = true;
        if (s == OpacitySlider) OpacityNum.Value = (decimal)s.Value;
        else if (s == GlassSlider) GlassNum.Value = (decimal)s.Value;
        else if (s == BorderSlider) BorderNum.Value = (decimal)s.Value;
        else if (s == GlowSlider) GlowNum.Value = (decimal)s.Value;
        else if (s == RadiusSlider) RadiusNum.Value = (decimal)s.Value;
        else if (s == WidthSlider) WidthNum.Value = (decimal)s.Value;
        else if (s == HeightSlider) HeightNum.Value = (decimal)s.Value;
        else if (s == MinWSlider) MinWNum.Value = (decimal)s.Value;
        else if (s == MaxWSlider) MaxWNum.Value = (decimal)s.Value;
        else if (s == OffXSlider) OffXNum.Value = (decimal)s.Value;
        else if (s == OffYSlider) OffYNum.Value = (decimal)s.Value;
        else if (s == WeatherMinSlider) WeatherMinNum.Value = (decimal)s.Value;
        else if (s == VolumeSlider) VolumeNum.Value = (decimal)s.Value;
        else if (s == NotifySlider) NotifyNum.Value = (decimal)s.Value;
        else if (s == ChatSlider) ChatNum.Value = (decimal)s.Value;
        else if (s == CallSlider) CallNum.Value = (decimal)s.Value;
        else if (s == ExpandMsSlider) ExpandMsNum.Value = (decimal)s.Value;
        else if (s == CollapseMsSlider) CollapseMsNum.Value = (decimal)s.Value;
        _syncing = false;
    }

    private void SyncFromNum(NumericUpDown n)
    {
        var v = (double)(n.Value ?? 0);
        _syncing = true;
        if (n == OpacityNum) OpacitySlider.Value = v;
        else if (n == GlassNum) GlassSlider.Value = v;
        else if (n == BorderNum) BorderSlider.Value = v;
        else if (n == GlowNum) GlowSlider.Value = v;
        else if (n == RadiusNum) RadiusSlider.Value = v;
        else if (n == WidthNum) WidthSlider.Value = v;
        else if (n == HeightNum) HeightSlider.Value = v;
        else if (n == MinWNum) MinWSlider.Value = v;
        else if (n == MaxWNum) MaxWSlider.Value = v;
        else if (n == OffXNum) OffXSlider.Value = v;
        else if (n == OffYNum) OffYSlider.Value = v;
        else if (n == WeatherMinNum) WeatherMinSlider.Value = v;
        else if (n == VolumeNum) VolumeSlider.Value = v;
        else if (n == NotifyNum) NotifySlider.Value = v;
        else if (n == ChatNum) ChatSlider.Value = v;
        else if (n == CallNum) CallSlider.Value = v;
        else if (n == ExpandMsNum) ExpandMsSlider.Value = v;
        else if (n == CollapseMsNum) CollapseMsSlider.Value = v;
        _syncing = false;
    }

    private void OnCityLost(object? sender, RoutedEventArgs e)
    {
        if (!_boot) return;
        Commit();
    }

    private void OnPreviewSound(object? sender, RoutedEventArgs e) => IslandSounds.Cue(IslandSound.Notify);

    private void OnOpenToastSettings(object? sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:notifications") { UseShellExecute = true });
        }
        catch
        {
        }
    }

    private void OnPreviewAnim(object? sender, RoutedEventArgs e)
    {
        IslandAnimator.FastInvoke(PreviewPill);
        IslandHost.Overlay?.PreviewAnimation();
    }

    private void Commit()
    {
        PrefsStore.Mutate(p =>
        {
            var pi = Math.Clamp(PaletteBox.SelectedIndex, 0, PaletteCatalog.All.Length - 1);
            var fi = Math.Clamp(FontBox.SelectedIndex, 0, FontCatalog.All.Length - 1);
            p.PaletteId = PaletteCatalog.All[pi].Id;
            p.FontId = FontCatalog.All[fi].Id;
            p.IconStyle = IconBox.SelectedIndex switch
            {
                1 => "fluent-fill",
                2 => "mdl2",
                3 => "weather-soft",
                4 => "fluent-color",
                _ => "fluent"
            };
            p.Animation = AnimBox.SelectedIndex switch { 1 => "pulse", 2 => "breathe", 3 => "none", _ => "morph" };
            p.UiLanguage = LangBox.SelectedIndex switch { 1 => "en", 2 => "system", _ => "ru" };
            p.ExpandMs = (int)ExpandMsSlider.Value;
            p.CollapseMs = (int)CollapseMsSlider.Value;
            p.ClockFormat = ClockBox.SelectedIndex switch { 1 => "HH:mm:ss", 2 => "h:mm tt", _ => "HH:mm" };
            p.Density = DensityBox.SelectedIndex == 1 ? "compact" : "comfort";
            p.BadgeStyle = BadgeStyleBox.SelectedIndex switch { 1 => "count", 2 => "dot", _ => "icon-count" };
            p.WeatherPosition = WeatherPosBox.SelectedIndex switch { 1 => "hide", 2 => "expand", _ => "right" };
            p.AccentHex = AccentBox.Text ?? "";
            p.Opacity = OpacitySlider.Value;
            p.Glass = GlassSlider.Value;
            p.BorderThickness = BorderSlider.Value;
            p.GlowStrength = GlowSlider.Value;
            p.CornerRadius = RadiusSlider.Value;
            p.IdleWidth = WidthSlider.Value;
            p.IdleHeight = HeightSlider.Value;
            p.ExpandHeight = ExpandHeightBox.IsChecked == true;
            p.ExpandMode = p.ExpandHeight ? "both" : "width";
            p.MinWidth = MinWSlider.Value;
            p.MaxWidth = MaxWSlider.Value;
            p.TextScale = TextSizeBox.SelectedIndex switch { 0 => "small", 2 => "large", _ => "medium" };
            p.IconScale = IconSizeBox.SelectedIndex switch { 0 => "small", 2 => "large", _ => "medium" };
            p.ClickMode = ClickBox.SelectedIndex switch
            {
                1 => "cycle-player-center",
                2 => "center",
                3 => "player",
                4 => "off",
                _ => "cycle-center-player"
            };
            p.ShowAppBadge = BadgeBox.IsChecked == true;
            p.AnchorH = AnchorHBox.SelectedIndex switch { 0 => "left", 2 => "right", _ => "center" };
            p.AnchorV = AnchorVBox.SelectedIndex switch { 1 => "center", 2 => "bottom", _ => "top" };
            p.OffsetX = OffXSlider.Value;
            p.OffsetY = OffYSlider.Value;
            p.Layer = LayerBox.SelectedIndex switch { 1 => "normal", 2 => "desktop", _ => "topmost" };
            p.ScreenIndex = ScreenBox.SelectedIndex - 1;
            p.SuppressFocusAssist = QuietFocusBox.IsChecked == true;
            p.SuppressFullscreen = QuietFullBox.IsChecked == true;
            p.ReduceMotion = ReduceMotionBox.IsChecked == true;
            p.Diagnostics = DiagBox.IsChecked == true;
            p.RenderMode = RenderModeBox.SelectedIndex == 1 ? "resizeHost" : "fixedHost";
            p.NotifyDurationMs = (int)NotifySlider.Value;
            p.ChatDurationMs = (int)ChatSlider.Value;
            p.CallDurationMs = (int)CallSlider.Value;
            p.HoverPeek = HoverBox.IsChecked == true;
            p.AutoStart = AutoBox.IsChecked == true;
            p.ListenToasts = ToastBox.IsChecked == true;
            p.ShowWeather = WeatherBox.IsChecked == true;
            p.UseWindowsLocation = LocationBox.IsChecked == true;
            p.WeatherCity = CityBox.Text ?? "";
            p.WeatherIntervalMin = (int)WeatherMinSlider.Value;
            p.SoundsEnabled = SoundBox.IsChecked == true;
            p.SoundNotify = SoundNotifyBox.IsChecked == true;
            p.SoundChat = SoundChatBox.IsChecked == true;
            p.SoundError = SoundErrorBox.IsChecked == true;
            p.SoundComplete = SoundCompleteBox.IsChecked == true;
            p.SoundVolume = VolumeSlider.Value;
            p.BadgeApps = BadgeAppsBox.Text ?? "";
        });
        AutoStart.Set(PrefsStore.Current.AutoStart);
        Ui.Apply(PrefsStore.Current.UiLanguage);
        ApplyLang();
        _ = UpdateToastAsync();
        WeatherStatus.Text = "Weather: " + WeatherHub.Status;
        PaintPreview();
        PathHint.Text = Ui.T("saved") + PrefsStore.ActivePath;
    }

    private void OnExpandPreset(object? sender, SelectionChangedEventArgs e)
    {
        if (!_boot) return;
        ExpandMsSlider.Value = Motion.PresetMs(ExpandSpeedBox.SelectedIndex switch { 0 => "fast", 2 => "slow", _ => "normal" }, false);
        Commit();
    }

    private void OnCollapsePreset(object? sender, SelectionChangedEventArgs e)
    {
        if (!_boot) return;
        CollapseMsSlider.Value = Motion.PresetMs(CollapseSpeedBox.SelectedIndex switch { 0 => "fast", 2 => "slow", _ => "normal" }, true);
        Commit();
    }

    private void ApplyLang()
    {
        Title = Ui.T("title");
        HeadingText.Text = Ui.T("heading");
        AppearanceExp.Header = Ui.T("appearance");
        CapsuleExp.Header = Ui.T("capsule");
        PositionExp.Header = Ui.T("position");
        WeatherExp.Header = Ui.T("weather");
        SoundsExp.Header = Ui.T("sounds");
        NotifyExp.Header = Ui.T("notify");
        LangLabel.Text = Ui.T("lang");
        LangHint.Text = Ui.T("lang_h");
        RenderModeLabel.Text = Ui.T("render_mode");
        RenderModeHint.Text = Ui.T("render_mode_h");
        var rm = RenderModeBox.SelectedIndex;
        RenderModeBox.ItemsSource = new[] { Ui.T("render_fixed"), Ui.T("render_resize") };
        RenderModeBox.SelectedIndex = rm < 0 ? 0 : rm;
        PaletteLabel.Text = Ui.T("palette");
        AccentLabel.Text = Ui.T("accent");
        FontLabel.Text = Ui.T("font");
        IconsLabel.Text = Ui.T("icons");
        OpacityLabel.Text = Ui.T("opacity");
        GlassLabel.Text = Ui.T("glass");
        BorderLabel.Text = Ui.T("border");
        GlowLabel.Text = Ui.T("glow");
        RadiusLabel.Text = Ui.T("radius");
        MotionExtraLabel.Text = Ui.T("motion_extra");
        MotionExtraHint.Text = Ui.T("motion_extra_h");
        ExpandSpeedLabel.Text = Ui.T("expand_speed");
        ExpandSpeedHint.Text = Ui.T("expand_speed_h");
        CollapseSpeedLabel.Text = Ui.T("collapse_speed");
        CollapseSpeedHint.Text = Ui.T("collapse_speed_h");
        ClockLabel.Text = Ui.T("clock");
        DensityLabel.Text = Ui.T("density");
        IdleWLabel.Text = Ui.T("idle_w");
        IdleHLabel.Text = Ui.T("idle_h");
        ExpandHeightBox.Content = Ui.T("expand_h");
        MinWLabel.Text = Ui.T("min_w");
        MaxWLabel.Text = Ui.T("max_w");
        TextSizeLabel.Text = Ui.T("text_size");
        IconSizeLabel.Text = Ui.T("icon_size");
        BadgeBox.Content = Ui.T("badge");
        BadgeStyleLabel.Text = Ui.T("badge_style");
        AnchorHLabel.Text = Ui.T("anchor_h");
        AnchorVLabel.Text = Ui.T("anchor_v");
        OffXLabel.Text = Ui.T("off_x");
        OffYLabel.Text = Ui.T("off_y");
        LayerLabel.Text = Ui.T("layer");
        LayerHint.Text = Ui.T("layer_h");
        ScreenLabel.Text = Ui.T("display");
        AutoBox.Content = Ui.T("autostart");
        ToastBox.Content = Ui.T("toasts");
        ToastHint.Text = Ui.T("toasts_h");
        HoverBox.Content = Ui.T("hover");
        HoverHint.Text = Ui.T("hover_h");
        OpenToastBtn.Content = Ui.T("open_toast");
        OpenPrivacyBtn.Content = Ui.T("open_privacy");
        BadgeAppsLabel.Text = Ui.T("badge_apps");
        BadgeAppsHint.Text = Ui.T("badge_apps_h");
        BadgeAppsBox.Watermark = Ui.T("badge_apps");
        NotifyMsLabel.Text = Ui.T("notify_ms");
        ChatMsLabel.Text = Ui.T("chat_ms");
        CallMsLabel.Text = Ui.T("call_ms");
        ClickLabel.Text = Ui.T("click");
        QuietFocusBox.Content = Ui.T("quiet_focus");
        QuietFullBox.Content = Ui.T("quiet_full");
        ReduceMotionBox.Content = Ui.T("reduce_motion");
        DiagBox.Content = Ui.T("diagnostics");
        WeatherBox.Content = Ui.T("weather_on");
        LocationBox.Content = Ui.T("location");
        CityBox.Watermark = Ui.T("city");
        WxMinLabel.Text = Ui.T("wx_min");
        SoundBox.Content = Ui.T("sounds_on");
        SoundNotifyBox.Content = Ui.T("snd_notify");
        SoundChatBox.Content = Ui.T("snd_chat");
        SoundErrorBox.Content = Ui.T("snd_error");
        SoundCompleteBox.Content = Ui.T("snd_complete");
        CueVolLabel.Text = Ui.T("cue_vol");
        PreviewSoundBtn.Content = Ui.T("preview_sound");
        SysVolLabel.Text = Ui.T("sys_vol");
        SysMuteBox.Content = Ui.T("sys_mute");
        PreviewAnimBtn.Content = Ui.T("preview_anim");
        WhatsNewTitle.Text = Ui.T("whats_new_title");
        WhatsNewBody.Text = Ui.T("whats_new_body");
        WhatsNewOk.Content = Ui.T("whats_new_ok");
        PathHint.Text = Ui.T("saved") + PrefsStore.ActivePath;
        foreach (var u in this.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("unit")))
        {
            var t = u.Text ?? "";
            if (t is "%" or "pct") u.Text = Ui.T("unit_pct");
            else if (t is "px") u.Text = Ui.T("unit_px");
            else if (t is "мс" or "ms") u.Text = Ui.T("unit_ms");
            else if (t is "мин" or "min") u.Text = Ui.T("unit_min");
        }
    }

    private void OnWhatsNewOk(object? sender, RoutedEventArgs e)
    {
        PrefsStore.Mutate(p => p.LastSeenVersion = "1.3.1");
        WhatsNewBox.IsVisible = false;
    }

    private void OnCheck(object? sender, RoutedEventArgs e)
    {
        if (!_boot) return;
        Commit();
    }

    private async System.Threading.Tasks.Task UpdateToastAsync()
    {
        await ToastHub.RefreshAsync(PrefsStore.Current.ListenToasts);
        ToastStatus.Text = PrefsStore.Current.ListenToasts
            ? Ui.T("toast_status") + ToastHub.Status + (ToastHub.Allowed ? "" : " — " + Ui.T("toast_denied"))
            : Ui.T("toast_off");
        _ = AppBadgeHub.RefreshAsync();
    }

    private void PaintPreview()
    {
        var pal = PaletteCatalog.Get(PrefsStore.Current.PaletteId);
        var font = FontCatalog.Get(PrefsStore.Current.FontId);
        var p = PrefsStore.Current;
        PreviewPill.Background = new SolidColorBrush(pal.Background, p.Opacity * (1 - p.Glass * 0.35));
        PreviewPill.BorderBrush = new SolidColorBrush(pal.Border);
        PreviewPill.BorderThickness = new Thickness(p.BorderThickness);
        var h = p.IdleHeight;
        PreviewPill.Height = h;
        PreviewPill.CornerRadius = new CornerRadius(!p.ExpandHeight || p.CornerRadius <= 0 ? h / 2 : p.CornerRadius);
        PreviewClock.Foreground = new SolidColorBrush(pal.Text);
        PreviewClock.FontFamily = new FontFamily(font.Family);
        PreviewClock.Text = DateTime.Now.ToString(p.ClockFormat, System.Globalization.CultureInfo.InvariantCulture);
        PreviewIcon.Fill = new SolidColorBrush(PaletteCatalog.AccentOf(p));
        PreviewIcon.Data = IslandIcons.Geometry(IslandGlyph.Chat, IslandIcons.Normalize(p.IconStyle));
    }

    public void SyncMasterVolume()
    {
        if (!SystemVolume.TryGet(out var s, out var mute))
        {
            SysVolStatus.Text = Ui.T("sys_vol_st") + SystemVolume.Status;
            return;
        }
        _syncing = true;
        SysVolSlider.Value = s;
        SysVolNum.Value = (decimal)s;
        SysMuteBox.IsChecked = mute;
        _syncing = false;
        SysVolStatus.Text = Ui.T("sys_vol_st") + SystemVolume.Status;
    }

    private void OnSysVol(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!_boot || _syncing || e.Property != Slider.ValueProperty) return;
        SystemVolume.TrySet((float)SysVolSlider.Value);
        _syncing = true;
        SysVolNum.Value = (decimal)SysVolSlider.Value;
        _syncing = false;
        SysVolStatus.Text = Ui.T("sys_vol_st") + SystemVolume.Status;
    }

    private void OnSysVolNum(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (!_boot || _syncing) return;
        var v = (double)(SysVolNum.Value ?? 0);
        SystemVolume.TrySet((float)v);
        _syncing = true;
        SysVolSlider.Value = v;
        _syncing = false;
        SysVolStatus.Text = Ui.T("sys_vol_st") + SystemVolume.Status;
    }

    private void OnSysMute(object? sender, RoutedEventArgs e)
    {
        if (!_boot || _syncing) return;
        SystemVolume.TrySetMute(SysMuteBox.IsChecked == true);
        SyncMasterVolume();
    }

    private async void OnEnableToast(object? sender, RoutedEventArgs e)
    {
        var code = await ToastIdentity.TryRegisterSparseAsync();
        IslandLog.Write("toast", "ui-register " + code);
        ToastStatus.Text = Ui.T("toast_unsigned");
        ToastIdentity.OpenPrivacySettings();
    }

    private void OnOpenToastPrivacy(object? sender, RoutedEventArgs e) => ToastIdentity.OpenPrivacySettings();

    private static int itemsCount(ComboBox box)
    {
        if (box.ItemsSource is System.Collections.ICollection c) return c.Count;
        return box.ItemCount;
    }

    private static int IndexOf<T>(T[] items, Func<T, bool> pred)
    {
        for (var i = 0; i < items.Length; i++)
            if (pred(items[i])) return i;
        return 0;
    }
}
