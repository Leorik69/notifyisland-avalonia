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

    public SettingsWindow()
    {
        InitializeComponent();
        Opened += (_, _) => Win32Overlay.ApplyNormalChrome(this);
        PaletteBox.ItemsSource = Array.ConvertAll(PaletteCatalog.All, p => p.Name);
        FontBox.ItemsSource = Array.ConvertAll(FontCatalog.All, f => f.Name);
        IconBox.ItemsSource = new[] { "Fluent outline", "Fluent filled", "Segoe MDL2", "Weather soft" };
        AnimBox.ItemsSource = new[] { "Morph + pulse", "Pulse", "Breathe", "Morph only" };
        SpeedBox.ItemsSource = new[] { "Fast (180ms)", "Normal (260ms)" };
        ClockBox.ItemsSource = new[] { "HH:mm", "HH:mm:ss", "h:mm tt" };
        AnchorHBox.ItemsSource = new[] { "Left", "Center", "Right" };
        AnchorVBox.ItemsSource = new[] { "Top", "Center", "Bottom" };
        LayerBox.ItemsSource = new[] { "Always on top", "Normal window", "Desktop (HWND_BOTTOM)" };
        LoadFromPrefs();
        WeatherHub.Changed += OnWeatherStatus;
        Closed += (_, _) => WeatherHub.Changed -= OnWeatherStatus;
        IslandAnimator.WireLayout(PreviewPill, Motion.MorphMs);
        _boot = true;
        PaintPreview();
        PathHint.Text = "Saved to " + PrefsStore.ActivePath;
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
            _ => 0
        };
        AnimBox.SelectedIndex = p.Animation switch { "pulse" => 1, "breathe" => 2, "none" => 3, _ => 0 };
        SpeedBox.SelectedIndex = p.AnimSpeed == "fast" ? 0 : 1;
        ClockBox.SelectedIndex = p.ClockFormat switch { "HH:mm:ss" => 1, "h:mm tt" => 2, _ => 0 };
        OpacitySlider.Value = p.Opacity;
        BorderSlider.Value = p.BorderThickness;
        GlowSlider.Value = p.GlowStrength;
        RadiusSlider.Value = p.CornerRadius;
        WidthSlider.Value = p.IdleWidth;
        HeightSlider.Value = p.IdleHeight;
        ExpandHeightBox.IsChecked = p.ExpandHeight;
        MinWSlider.Value = p.MinWidth;
        MaxWSlider.Value = p.MaxWidth;
        AnchorHBox.SelectedIndex = p.AnchorH switch { "left" => 0, "right" => 2, _ => 1 };
        AnchorVBox.SelectedIndex = p.AnchorV switch { "center" => 1, "bottom" => 2, _ => 0 };
        OffXSlider.Value = p.OffsetX;
        OffYSlider.Value = p.OffsetY;
        LayerBox.SelectedIndex = p.Layer switch { "normal" => 1, "desktop" => 2, _ => 0 };
        NotifySlider.Value = p.NotifyDurationMs;
        HoverBox.IsChecked = p.HoverPeek;
        ActionCenterBox.IsChecked = p.ClickOpensActionCenter;
        AutoBox.IsChecked = p.AutoStart || AutoStart.IsEnabled();
        ToastBox.IsChecked = p.ListenToasts;
        ToastStatus.Text = "Toasts: " + ToastHub.Status;
        WeatherBox.IsChecked = p.ShowWeather;
        LocationBox.IsChecked = p.UseWindowsLocation;
        CityBox.Text = p.WeatherCity;
        WeatherMinSlider.Value = p.WeatherIntervalMin;
        WeatherStatus.Text = "Weather: " + WeatherHub.Status;
        SoundBox.IsChecked = p.SoundsEnabled;
        VolumeSlider.Value = p.SoundVolume;
    }

    private void OnWeatherStatus()
    {
        Dispatcher.UIThread.Post(() =>
        {
            WeatherStatus.Text = "Weather: " + WeatherHub.Status;
        });
    }

    private void OnChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_boot) return;
        Commit();
        IslandAnimator.Pulse(PreviewPill, Motion.PulseMs);
    }

    private void OnSlider(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!_boot || e.Property != Slider.ValueProperty) return;
        Commit();
    }

    private void OnCityLost(object? sender, RoutedEventArgs e)
    {
        if (!_boot) return;
        Commit();
    }

    private void OnPreviewSound(object? sender, RoutedEventArgs e) => IslandSounds.Cue(IslandSound.Notify);

    private void OnPreviewAnim(object? sender, RoutedEventArgs e)
    {
        IslandAnimator.Pulse(PreviewPill, Motion.PulseMs);
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
                _ => "fluent"
            };
            p.Animation = AnimBox.SelectedIndex switch { 1 => "pulse", 2 => "breathe", 3 => "none", _ => "morph" };
            p.AnimSpeed = SpeedBox.SelectedIndex == 0 ? "fast" : "normal";
            p.ClockFormat = ClockBox.SelectedIndex switch { 1 => "HH:mm:ss", 2 => "h:mm tt", _ => "HH:mm" };
            p.Opacity = OpacitySlider.Value;
            p.BorderThickness = BorderSlider.Value;
            p.GlowStrength = GlowSlider.Value;
            p.CornerRadius = RadiusSlider.Value;
            p.IdleWidth = WidthSlider.Value;
            p.IdleHeight = HeightSlider.Value;
            p.ExpandHeight = ExpandHeightBox.IsChecked == true;
            p.ExpandMode = p.ExpandHeight ? "both" : "width";
            p.MinWidth = MinWSlider.Value;
            p.MaxWidth = MaxWSlider.Value;
            p.AnchorH = AnchorHBox.SelectedIndex switch { 0 => "left", 2 => "right", _ => "center" };
            p.AnchorV = AnchorVBox.SelectedIndex switch { 1 => "center", 2 => "bottom", _ => "top" };
            p.OffsetX = OffXSlider.Value;
            p.OffsetY = OffYSlider.Value;
            p.Layer = LayerBox.SelectedIndex switch { 1 => "normal", 2 => "desktop", _ => "topmost" };
            p.NotifyDurationMs = (int)NotifySlider.Value;
            p.HoverPeek = HoverBox.IsChecked == true;
            p.ClickOpensActionCenter = ActionCenterBox.IsChecked == true;
            p.AutoStart = AutoBox.IsChecked == true;
            p.ListenToasts = ToastBox.IsChecked == true;
            p.ShowWeather = WeatherBox.IsChecked == true;
            p.UseWindowsLocation = LocationBox.IsChecked == true;
            p.WeatherCity = CityBox.Text ?? "";
            p.WeatherIntervalMin = (int)WeatherMinSlider.Value;
            p.SoundsEnabled = SoundBox.IsChecked == true;
            p.SoundVolume = VolumeSlider.Value;
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
            ? "Toasts: " + ToastHub.Status + (ToastHub.Allowed ? "" : " — demo hub still works (F9). Not faking access.")
            : "Toasts: Off";
    }

    private void PaintPreview()
    {
        var pal = PaletteCatalog.Get(PrefsStore.Current.PaletteId);
        var font = FontCatalog.Get(PrefsStore.Current.FontId);
        var p = PrefsStore.Current;
        PreviewPill.Background = new SolidColorBrush(pal.Background, p.Opacity);
        PreviewPill.BorderBrush = new SolidColorBrush(pal.Border);
        PreviewPill.BorderThickness = new Thickness(p.BorderThickness);
        var h = p.IdleHeight;
        PreviewPill.Height = h;
        PreviewPill.CornerRadius = new CornerRadius(!p.ExpandHeight || p.CornerRadius <= 0 ? h / 2 : p.CornerRadius);
        PreviewClock.Foreground = new SolidColorBrush(pal.Text);
        PreviewClock.FontFamily = new FontFamily(font.Family);
        PreviewClock.Text = DateTime.Now.ToString(p.ClockFormat, System.Globalization.CultureInfo.InvariantCulture);
        PreviewIcon.Fill = new SolidColorBrush(pal.Accent);
        PreviewIcon.Data = IslandIcons.Geometry(IslandGlyph.Notify, IslandIcons.Normalize(p.IconStyle));
    }

    private static int IndexOf<T>(T[] items, Func<T, bool> pred)
    {
        for (var i = 0; i < items.Length; i++)
            if (pred(items[i])) return i;
        return 0;
    }
}
