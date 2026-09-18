using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

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
        PaletteBox.ItemsSource = Array.ConvertAll(PaletteCatalog.All, p => p.Name);
        FontBox.ItemsSource = Array.ConvertAll(FontCatalog.All, f => f.Name);
        IconBox.ItemsSource = new[] { "Fluent outline", "Fluent filled", "Segoe MDL2", "Weather soft", "Fluent color" };
        AnimBox.ItemsSource = new[] { "Morph + invoke", "Invoke", "Breathe", "Morph only" };
        SpeedBox.ItemsSource = new[] { "Fast (167ms)", "Normal (250ms)" };
        ClockBox.ItemsSource = new[] { "HH:mm", "HH:mm:ss", "h:mm tt" };
        DensityBox.ItemsSource = new[] { "Comfort", "Compact" };
        BadgeStyleBox.ItemsSource = new[] { "Icon + count", "Count only", "Dot" };
        WeatherPosBox.ItemsSource = new[] { "Right of clock", "Hide", "Expand only" };
        AnchorHBox.ItemsSource = new[] { "Left", "Center", "Right" };
        AnchorVBox.ItemsSource = new[] { "Top", "Center", "Bottom" };
        LayerBox.ItemsSource = new[] { "Always on top", "Normal window", "Desktop (HWND_BOTTOM)" };
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
        _boot = true;
        PaintPreview();
        PathHint.Text = "Saved to " + PrefsStore.ActivePath;
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
        SpeedBox.SelectedIndex = p.AnimSpeed == "fast" ? 0 : 1;
        ClockBox.SelectedIndex = p.ClockFormat switch { "HH:mm:ss" => 1, "h:mm tt" => 2, _ => 0 };
        DensityBox.SelectedIndex = p.Density == "compact" ? 1 : 0;
        BadgeStyleBox.SelectedIndex = p.BadgeStyle switch { "count" => 1, "dot" => 2, _ => 0 };
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
        Pair(GlyphSlider, GlyphNum, p.GlyphSize);
        BadgeBox.IsChecked = p.ShowAppBadge;
        AnchorHBox.SelectedIndex = p.AnchorH switch { "left" => 0, "right" => 2, _ => 1 };
        AnchorVBox.SelectedIndex = p.AnchorV switch { "center" => 1, "bottom" => 2, _ => 0 };
        Pair(OffXSlider, OffXNum, p.OffsetX);
        Pair(OffYSlider, OffYNum, p.OffsetY);
        LayerBox.SelectedIndex = p.Layer switch { "normal" => 1, "desktop" => 2, _ => 0 };
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
    }

    private void OnWeatherStatus() => Dispatcher.UIThread.Post(() => WeatherStatus.Text = "Weather: " + WeatherHub.Status);

    private void OnChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_boot) return;
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
        else if (s == GlyphSlider) GlyphNum.Value = (decimal)s.Value;
        else if (s == OffXSlider) OffXNum.Value = (decimal)s.Value;
        else if (s == OffYSlider) OffYNum.Value = (decimal)s.Value;
        else if (s == WeatherMinSlider) WeatherMinNum.Value = (decimal)s.Value;
        else if (s == VolumeSlider) VolumeNum.Value = (decimal)s.Value;
        else if (s == NotifySlider) NotifyNum.Value = (decimal)s.Value;
        else if (s == ChatSlider) ChatNum.Value = (decimal)s.Value;
        else if (s == CallSlider) CallNum.Value = (decimal)s.Value;
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
        else if (n == GlyphNum) GlyphSlider.Value = v;
        else if (n == OffXNum) OffXSlider.Value = v;
        else if (n == OffYNum) OffYSlider.Value = v;
        else if (n == WeatherMinNum) WeatherMinSlider.Value = v;
        else if (n == VolumeNum) VolumeSlider.Value = v;
        else if (n == NotifyNum) NotifySlider.Value = v;
        else if (n == ChatNum) ChatSlider.Value = v;
        else if (n == CallNum) CallSlider.Value = v;
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
            p.AnimSpeed = SpeedBox.SelectedIndex == 0 ? "fast" : "normal";
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
            p.GlyphSize = GlyphSlider.Value;
            p.ShowAppBadge = BadgeBox.IsChecked == true;
            p.AnchorH = AnchorHBox.SelectedIndex switch { 0 => "left", 2 => "right", _ => "center" };
            p.AnchorV = AnchorVBox.SelectedIndex switch { 1 => "center", 2 => "bottom", _ => "top" };
            p.OffsetX = OffXSlider.Value;
            p.OffsetY = OffYSlider.Value;
            p.Layer = LayerBox.SelectedIndex switch { 1 => "normal", 2 => "desktop", _ => "topmost" };
            p.NotifyDurationMs = (int)NotifySlider.Value;
            p.ChatDurationMs = (int)ChatSlider.Value;
            p.CallDurationMs = (int)CallSlider.Value;
            p.HoverPeek = HoverBox.IsChecked == true;
            p.ClickOpensActionCenter = ActionCenterBox.IsChecked == true;
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
        _ = UpdateToastAsync();
        WeatherStatus.Text = "Weather: " + WeatherHub.Status;
        PaintPreview();
        PathHint.Text = "Saved to " + PrefsStore.ActivePath;
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
            ? "Toasts: " + ToastHub.Status + (ToastHub.Allowed ? "" : " — F9 demo still works. Not faking access.")
            : "Toasts: Off";
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

    private static int IndexOf<T>(T[] items, Func<T, bool> pred)
    {
        for (var i = 0; i < items.Length; i++)
            if (pred(items[i])) return i;
        return 0;
    }
}
