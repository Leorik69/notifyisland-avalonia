using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

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
        IconBox.ItemsSource = new[] { "Fluent paths", "Segoe MDL2", "Minimal" };
        AnimBox.ItemsSource = new[] { "Morph + pulse", "Pulse", "Breathe", "Morph only" };
        SpeedBox.ItemsSource = new[] { "Fast (180ms)", "Normal (260ms)" };
        ClockBox.ItemsSource = new[] { "HH:mm", "HH:mm:ss", "h:mm tt" };
        LoadFromPrefs();
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
        IconBox.SelectedIndex = p.IconStyle switch { "mdl2" => 1, "minimal" => 2, _ => 0 };
        AnimBox.SelectedIndex = p.Animation switch { "pulse" => 1, "breathe" => 2, "none" => 3, _ => 0 };
        SpeedBox.SelectedIndex = p.AnimSpeed == "fast" ? 0 : 1;
        ClockBox.SelectedIndex = p.ClockFormat switch { "HH:mm:ss" => 1, "h:mm tt" => 2, _ => 0 };
        OpacitySlider.Value = p.Opacity;
        BorderSlider.Value = p.BorderThickness;
        GlowSlider.Value = p.GlowStrength;
        RadiusSlider.Value = p.CornerRadius;
        WidthSlider.Value = p.IdleWidth;
        HeightSlider.Value = p.IdleHeight;
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
            p.IconStyle = IconBox.SelectedIndex switch { 1 => "mdl2", 2 => "minimal", _ => "fluent" };
            p.Animation = AnimBox.SelectedIndex switch { 1 => "pulse", 2 => "breathe", 3 => "none", _ => "morph" };
            p.AnimSpeed = SpeedBox.SelectedIndex == 0 ? "fast" : "normal";
            p.ClockFormat = ClockBox.SelectedIndex switch { 1 => "HH:mm:ss", 2 => "h:mm tt", _ => "HH:mm" };
            p.Opacity = OpacitySlider.Value;
            p.BorderThickness = BorderSlider.Value;
            p.GlowStrength = GlowSlider.Value;
            p.CornerRadius = RadiusSlider.Value;
            p.IdleWidth = WidthSlider.Value;
            p.IdleHeight = HeightSlider.Value;
        });
        PaintPreview();
        PathHint.Text = "Saved to " + PrefsStore.ActivePath;
    }

    private void PaintPreview()
    {
        var pal = PaletteCatalog.Get(PrefsStore.Current.PaletteId);
        var font = FontCatalog.Get(PrefsStore.Current.FontId);
        var p = PrefsStore.Current;
        PreviewPill.Background = new SolidColorBrush(pal.Background, p.Opacity);
        PreviewPill.BorderBrush = new SolidColorBrush(pal.Border);
        PreviewPill.BorderThickness = new Thickness(p.BorderThickness);
        var h = Math.Max(36, p.IdleHeight + 10);
        PreviewPill.Height = h;
        PreviewPill.CornerRadius = new CornerRadius(p.CornerRadius <= 0 ? h / 2 : p.CornerRadius);
        PreviewClock.Foreground = new SolidColorBrush(pal.Text);
        PreviewClock.FontFamily = new FontFamily(font.Family);
        PreviewClock.Text = DateTime.Now.ToString(p.ClockFormat, System.Globalization.CultureInfo.InvariantCulture);
        PreviewIcon.Fill = new SolidColorBrush(pal.Accent);
        PreviewIcon.Data = IslandIcons.Geometry(IslandGlyph.Notify, p.IconStyle);
    }

    private static int IndexOf<T>(T[] items, Func<T, bool> pred)
    {
        for (var i = 0; i < items.Length; i++)
            if (pred(items[i])) return i;
        return 0;
    }
}
