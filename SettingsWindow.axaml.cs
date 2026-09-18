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
        AnimBox.ItemsSource = new[] { "Morph", "Pulse", "Breathe", "None" };
        LoadFromPrefs();
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
        OpacitySlider.Value = p.Opacity;
    }

    private void OnChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_boot) return;
        Commit();
    }

    private void OnOpacityChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!_boot || e.Property != Slider.ValueProperty) return;
        Commit();
    }

    private void OnPreviewAnim(object? sender, RoutedEventArgs e)
    {
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
            p.Opacity = OpacitySlider.Value;
        });
        PaintPreview();
        PathHint.Text = "Saved to " + PrefsStore.ActivePath;
    }

    private void PaintPreview()
    {
        var pal = PaletteCatalog.Get(PrefsStore.Current.PaletteId);
        var font = FontCatalog.Get(PrefsStore.Current.FontId);
        PreviewPill.Background = new SolidColorBrush(pal.Background, PrefsStore.Current.Opacity);
        PreviewPill.BorderBrush = new SolidColorBrush(pal.Border);
        PreviewPill.BorderThickness = new Thickness(1);
        PreviewClock.Foreground = new SolidColorBrush(pal.Text);
        PreviewClock.FontFamily = new FontFamily(font.Family);
        PreviewIcon.Fill = new SolidColorBrush(pal.Accent);
        PreviewIcon.Data = IslandIcons.Geometry(IslandGlyph.Notify, PrefsStore.Current.IconStyle);
    }

    private static int IndexOf<T>(T[] items, Func<T, bool> pred)
    {
        for (var i = 0; i < items.Length; i++)
            if (pred(items[i])) return i;
        return 0;
    }
}
