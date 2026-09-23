using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace NotifyIsland;

/// <summary>
/// Full Settings Avalonia Window (not tray flyout / not ContextMenu dump).
/// Single-instance: OverlayWindow.OpenSettings activates existing if open.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _live;
    private readonly AppSettings _draft;
    private readonly Action<AppSettings> _onApply;
    private readonly Action? _onDemoBattery;
    private bool _paletteWired;
    private bool _loadingUi;

    public SettingsWindow() : this(new AppSettings(), _ => { }) { }

    public SettingsWindow(AppSettings live, Action<AppSettings> onApply, Action? onDemoBattery = null)
    {
        _live = live;
        _draft = new AppSettings();
        live.CopyTo(_draft);
        _onApply = onApply;
        _onDemoBattery = onDemoBattery;
        InitializeComponent();
        RestoreGeometry();
        LoadUi();
        WireVolumeLabels();
        WirePalettePreview();
        Closing += OnClosing;
    }

    private void WireVolumeLabels()
    {
        BindPct(OpacitySlider, OpacityLabel, v => $"{(int)v}%");
        BindPct(VolumeSlider, VolumeLabel, v => $"{(int)v}%");
        BindPct(VolNotifySlider, VolNotifyLabel, v => $"{(int)v}%");
        BindPct(VolExpandSlider, VolExpandLabel, v => $"{(int)v}%");
        BindPct(VolCollapseSlider, VolCollapseLabel, v => $"{(int)v}%");
        BindPct(VolSwipeSlider, VolSwipeLabel, v => $"{(int)v}%");
        BindPct(VolErrorSlider, VolErrorLabel, v => $"{(int)v}%");
        BindPct(VolHoverSlider, VolHoverLabel, v => $"{(int)v}%");
        FontSizeSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
            {
                FontSizeLabel.Text = $"{(int)FontSizeSlider.Value}";
                UpdatePreview();
            }
        };
        LowBatterySlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
                LowBatteryLabel.Text = $"{(int)LowBatterySlider.Value}";
        };
    }

    private void WirePalettePreview()
    {
        if (_paletteWired) return;
        _paletteWired = true;
        ColorFillPicker.PropertyChanged += OnPalettePickerChanged;
        ColorAccentPicker.PropertyChanged += OnPalettePickerChanged;
        ColorTextPrimaryPicker.PropertyChanged += OnPalettePickerChanged;
        ColorTextSecondaryPicker.PropertyChanged += OnPalettePickerChanged;
        UpdatePreview();
    }

    private void OnPalettePickerChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name is "Color" or "HsvColor")
            UpdatePreview();
    }

    private void UpdatePreview()
    {
        try
        {
            PreviewPill.Background = new SolidColorBrush(ColorFillPicker.Color);
            PreviewDot.Fill = new SolidColorBrush(ColorAccentPicker.Color);
            PreviewPrimary.Foreground = new SolidColorBrush(ColorTextPrimaryPicker.Color);
            PreviewSecondary.Foreground = new SolidColorBrush(ColorTextSecondaryPicker.Color);
            var fs = FontSizeSlider.Value;
            PreviewPrimary.FontSize = fs;
            PreviewSecondary.FontSize = fs;
            PreviewPrimary.FontFamily = IslandFonts.Resolve(SelectedTag(FontFamilyBox));
            PreviewSecondary.FontFamily = PreviewPrimary.FontFamily;
        }
        catch
        {
            // design-time / partial init
        }
    }

    private void OnSwatchPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { Tag: string tag }) return;
        var parts = tag.Split('|');
        if (parts.Length != 2) return;
        var hex = parts[1];
        Color color;
        try { color = Color.Parse(hex); }
        catch { return; }
        switch (parts[0])
        {
            case "ColorCapsuleFill": ColorFillPicker.Color = color; break;
            case "ColorAccent": ColorAccentPicker.Color = color; break;
            case "ColorTextPrimary": ColorTextPrimaryPicker.Color = color; break;
            case "ColorTextSecondary": ColorTextSecondaryPicker.Color = color; break;
        }
        UpdatePreview();
    }

    private static void BindPct(Slider slider, TextBlock label, Func<double, string> fmt)
    {
        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty) label.Text = fmt(slider.Value);
        };
    }

    private void RestoreGeometry()
    {
        Width = Math.Clamp(_draft.SettingsWindowWidth, 400, 1200);
        Height = Math.Clamp(_draft.SettingsWindowHeight, 520, 1200);
        if (_draft.SettingsWindowX is int x && _draft.SettingsWindowY is int y)
            Position = new PixelPoint(x, y);
        else
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    private void PersistGeometry()
    {
        _live.SettingsWindowWidth = Width;
        _live.SettingsWindowHeight = Height;
        _live.SettingsWindowX = Position.X;
        _live.SettingsWindowY = Position.Y;
        _draft.SettingsWindowWidth = Width;
        _draft.SettingsWindowHeight = Height;
        _draft.SettingsWindowX = Position.X;
        _draft.SettingsWindowY = Position.Y;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        PersistGeometry();
        _live.Save();
    }

    private void LoadUi()
    {
        _loadingUi = true;
        try
        {
        IslandVisibleBox.IsChecked = _draft.IslandVisible;
        WeatherEnabledBox.IsChecked = _draft.WeatherEnabled;
        ShowNowPlayingBox.IsChecked = _draft.ShowNowPlaying;
        ShowBatteryAlertsBox.IsChecked = _draft.ShowBatteryAlerts;
        ShowBatteryInCollapsedBox.IsChecked = _draft.ShowBatteryInCollapsed;
        LowBatterySlider.Value = BatteryAlertLogic.ClampLowPercent(_draft.LowBatteryPercent);
        LowBatteryLabel.Text = $"{(int)LowBatterySlider.Value}";
        SoundEnabledBox.IsChecked = _draft.SoundEnabled;
        OffsetXBox.Value = _draft.OffsetX;
        OffsetYBox.Value = _draft.OffsetY;
        OpacitySlider.Value = Math.Round(_draft.Opacity * 100);
        FontSizeSlider.Value = Math.Clamp(_draft.FontSize, 10, 18);
        VolumeSlider.Value = Math.Round(_draft.SoundVolume * 100);
        VolNotifySlider.Value = Math.Round(_draft.SoundVolNotify * 100);
        VolExpandSlider.Value = Math.Round(_draft.SoundVolExpand * 100);
        VolCollapseSlider.Value = Math.Round(_draft.SoundVolCollapse * 100);
        VolSwipeSlider.Value = Math.Round(_draft.SoundVolSwipe * 100);
        VolErrorSlider.Value = Math.Round(_draft.SoundVolError * 100);
        VolHoverSlider.Value = Math.Round(_draft.SoundVolHover * 100);
        OpacityLabel.Text = $"{(int)OpacitySlider.Value}%";
        FontSizeLabel.Text = $"{(int)FontSizeSlider.Value}";
        VolumeLabel.Text = $"{(int)VolumeSlider.Value}%";
        VolNotifyLabel.Text = $"{(int)VolNotifySlider.Value}%";
        VolExpandLabel.Text = $"{(int)VolExpandSlider.Value}%";
        VolCollapseLabel.Text = $"{(int)VolCollapseSlider.Value}%";
        VolSwipeLabel.Text = $"{(int)VolSwipeSlider.Value}%";
        VolErrorLabel.Text = $"{(int)VolErrorSlider.Value}%";
        VolHoverLabel.Text = $"{(int)VolHoverSlider.Value}%";
        SelectByTag(WeatherSideBox, _draft.WeatherSide.ToString());
        SelectByTag(EdgeBox, _draft.Edge.ToString());
        SelectByTag(OrientationBox, _draft.Orientation.ToString());
        SelectByTag(ZOrderBox, _draft.ZOrderMode.ToString());
        SelectByTag(SoundPackBox, _draft.SoundPack.ToString());
        SelectByTag(AnimSpeedBox, _draft.AnimationSpeed.ToString());
        SelectByTag(AppearStyleBox, _draft.AppearStyle.ToString());
        SelectByTag(DismissStyleBox, _draft.DismissStyle.ToString());
        SelectByTag(AnimMorphInflateBox, _draft.AnimMorphInflate.ToString());
        SelectByTag(AnimMorphCollapseBox, _draft.AnimMorphCollapse.ToString());
        SelectByTag(AnimUnreadPulseBox, _draft.AnimUnreadPulse.ToString());
        SelectByTag(AnimIdleBreathBox, _draft.AnimIdleBreath.ToString());
        SelectByTag(AnimHoverBox, _draft.AnimHover.ToString());
        SelectByTag(AnimSwipeRubberBox, _draft.AnimSwipeRubber.ToString());
        AnimPulseEnabledBox.IsChecked = _draft.AnimPulseEnabled;
        AnimBreathEnabledBox.IsChecked = _draft.AnimBreathEnabled;
        SelectByTag(IconPackBox, _draft.IconPack);
        SelectByTag(FontFamilyBox, _draft.FontFamily);
        SelectByTag(DateFormatBox, _draft.DateFormat.ToString());
        SelectByTag(ThemePresetBox, _draft.ThemePreset.ToString());
        SelectByTag(WeatherLocationModeBox, _draft.WeatherLocationMode.ToString());
        WeatherLocationNameBox.Text = _draft.WeatherLocationName;
        LatitudeBox.Value = (decimal)_draft.Latitude;
        LongitudeBox.Value = (decimal)_draft.Longitude;
        SelectCityPreset(_draft.WeatherLocationName);
        UpdateManualLocationVisibility();
        SetPicker(ColorFillPicker, _draft.ColorCapsuleFill, "#080808");
        SetPicker(ColorAccentPicker, _draft.ColorAccent, "#3D9CF0");
        SetPicker(ColorTextPrimaryPicker, _draft.ColorTextPrimary, "#FFFFFF");
        SetPicker(ColorTextSecondaryPicker, _draft.ColorTextSecondary, "#C8C8CC");
        FontFamilyBox.SelectionChanged += (_, _) => UpdatePreview();
        UpdatePreview();
        }
        finally { _loadingUi = false; }
    }

    private void SelectCityPreset(string? name)
    {
        var n = (name ?? "").Trim();
        var found = false;
        for (var i = 0; i < WeatherCityPresetBox.ItemCount; i++)
        {
            if (WeatherCityPresetBox.Items[i] is ComboBoxItem item &&
                string.Equals(item.Tag?.ToString(), n, StringComparison.OrdinalIgnoreCase))
            {
                WeatherCityPresetBox.SelectedIndex = i;
                found = true;
                break;
            }
        }
        if (!found)
            SelectByTag(WeatherCityPresetBox, "__custom");
    }

    private void UpdateManualLocationVisibility()
    {
        var manual = string.Equals(SelectedTag(WeatherLocationModeBox), "Manual", StringComparison.OrdinalIgnoreCase);
        ManualLocationPanel.IsVisible = manual;
    }

    private void OnWeatherLocationModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi) return;
        UpdateManualLocationVisibility();
    }

    private void OnWeatherCityPresetChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi) return;
        var tag = SelectedTag(WeatherCityPresetBox);
        if (string.IsNullOrEmpty(tag) || tag == "__custom") return;
        WeatherLocationNameBox.Text = tag;
        var city = WeatherLocationPresets.FindByName(tag);
        if (city is not null)
        {
            LatitudeBox.Value = (decimal)city.Lat;
            LongitudeBox.Value = (decimal)city.Lon;
        }
    }

    private void OnThemePresetChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi) return;
        if (!Enum.TryParse<ThemePreset>(SelectedTag(ThemePresetBox), true, out var preset)) return;
        if (preset == ThemePreset.Custom) return;
        // Apply stock bundle into draft UI controls immediately
        var tmp = new AppSettings();
        _draft.CopyTo(tmp);
        ThemePresets.Apply(preset, tmp);
        PushThemedFieldsToUi(tmp);
    }

    private void OnGoCustomTheme(object? sender, RoutedEventArgs e)
    {
        SelectByTag(ThemePresetBox, nameof(ThemePreset.Custom));
    }

    private void PushThemedFieldsToUi(AppSettings s)
    {
        SetPicker(ColorFillPicker, s.ColorCapsuleFill, "#080808");
        SetPicker(ColorAccentPicker, s.ColorAccent, "#3D9CF0");
        SetPicker(ColorTextPrimaryPicker, s.ColorTextPrimary, "#FFFFFF");
        SetPicker(ColorTextSecondaryPicker, s.ColorTextSecondary, "#C8C8CC");
        SelectByTag(FontFamilyBox, s.FontFamily);
        FontSizeSlider.Value = Math.Clamp(s.FontSize, 10, 18);
        FontSizeLabel.Text = $"{(int)FontSizeSlider.Value}";
        SelectByTag(IconPackBox, s.IconPack);
        SelectByTag(AnimSpeedBox, s.AnimationSpeed.ToString());
        SelectByTag(AnimMorphInflateBox, s.AnimMorphInflate.ToString());
        SelectByTag(AnimMorphCollapseBox, s.AnimMorphCollapse.ToString());
        SelectByTag(AppearStyleBox, s.AppearStyle.ToString());
        SelectByTag(DismissStyleBox, s.DismissStyle.ToString());
        SelectByTag(DateFormatBox, s.DateFormat.ToString());
        SelectByTag(SoundPackBox, s.SoundPack.ToString());
        UpdatePreview();
    }

    private static void SetPicker(ColorPicker picker, string hex, string fallback)
    {
        try { picker.Color = Color.Parse(AppSettings.NormalizeHex(hex, fallback)); }
        catch { picker.Color = Color.Parse(fallback); }
    }

    private static void SelectByTag(ComboBox box, string tag)
    {
        for (var i = 0; i < box.ItemCount; i++)
        {
            if (box.Items[i] is ComboBoxItem item &&
                string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedIndex = i;
                return;
            }
        }
        if (box.ItemCount > 0) box.SelectedIndex = 0;
    }

    private static string? SelectedTag(ComboBox box) =>
        (box.SelectedItem as ComboBoxItem)?.Tag?.ToString();

    private static string ColorToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private void ReadUi()
    {
        _draft.IslandVisible = IslandVisibleBox.IsChecked == true;
        _draft.AllowDrag = false;
        _draft.WeatherEnabled = WeatherEnabledBox.IsChecked == true;
        _draft.ShowNowPlaying = ShowNowPlayingBox.IsChecked == true;
        _draft.ShowBatteryAlerts = ShowBatteryAlertsBox.IsChecked == true;
        _draft.ShowBatteryInCollapsed = ShowBatteryInCollapsedBox.IsChecked == true;
        _draft.LowBatteryPercent = BatteryAlertLogic.ClampLowPercent((int)LowBatterySlider.Value);
        _draft.SoundEnabled = SoundEnabledBox.IsChecked == true;
        _draft.OffsetX = (int)(OffsetXBox.Value ?? 0);
        _draft.OffsetY = (int)(OffsetYBox.Value ?? 0);
        _draft.Opacity = Math.Clamp(OpacitySlider.Value / 100.0, 0.35, 1.0);
        _draft.FontSize = Math.Clamp(FontSizeSlider.Value, 10, 18);
        _draft.SoundVolume = Math.Clamp(VolumeSlider.Value / 100.0, 0.0, 1.0);
        _draft.SoundVolNotify = Math.Clamp(VolNotifySlider.Value / 100.0, 0.0, 1.0);
        _draft.SoundVolExpand = Math.Clamp(VolExpandSlider.Value / 100.0, 0.0, 1.0);
        _draft.SoundVolCollapse = Math.Clamp(VolCollapseSlider.Value / 100.0, 0.0, 1.0);
        _draft.SoundVolSwipe = Math.Clamp(VolSwipeSlider.Value / 100.0, 0.0, 1.0);
        _draft.SoundVolError = Math.Clamp(VolErrorSlider.Value / 100.0, 0.0, 1.0);
        _draft.SoundVolHover = Math.Clamp(VolHoverSlider.Value / 100.0, 0.0, 1.0);
        PersistGeometry();

        if (Enum.TryParse<WeatherSide>(SelectedTag(WeatherSideBox), true, out var ws))
            _draft.WeatherSide = ws;
        if (Enum.TryParse<IslandEdge>(SelectedTag(EdgeBox), true, out var edge))
            _draft.Edge = edge;
        if (Enum.TryParse<IslandOrientation>(SelectedTag(OrientationBox), true, out var ori))
            _draft.Orientation = ori;
        if (Enum.TryParse<ZOrderMode>(SelectedTag(ZOrderBox), true, out var z))
            _draft.ZOrderMode = z;
        if (Enum.TryParse<SoundPack>(SelectedTag(SoundPackBox), true, out var pack))
            _draft.SoundPack = pack;
        if (Enum.TryParse<AnimationSpeed>(SelectedTag(AnimSpeedBox), true, out var anim))
            _draft.AnimationSpeed = anim;
        if (Enum.TryParse<NotifyAppearStyle>(SelectedTag(AppearStyleBox), true, out var ap))
            _draft.AppearStyle = ap;
        if (Enum.TryParse<NotifyDismissStyle>(SelectedTag(DismissStyleBox), true, out var ds))
            _draft.DismissStyle = ds;
        if (Enum.TryParse<AnimationSpeed>(SelectedTag(AnimMorphInflateBox), true, out var mi))
            _draft.AnimMorphInflate = mi;
        if (Enum.TryParse<AnimationSpeed>(SelectedTag(AnimMorphCollapseBox), true, out var mc))
            _draft.AnimMorphCollapse = mc;
        if (Enum.TryParse<AnimationSpeed>(SelectedTag(AnimUnreadPulseBox), true, out var up))
            _draft.AnimUnreadPulse = up;
        if (Enum.TryParse<AnimationSpeed>(SelectedTag(AnimIdleBreathBox), true, out var ib))
            _draft.AnimIdleBreath = ib;
        if (Enum.TryParse<AnimationSpeed>(SelectedTag(AnimHoverBox), true, out var hv))
            _draft.AnimHover = hv;
        if (Enum.TryParse<AnimationSpeed>(SelectedTag(AnimSwipeRubberBox), true, out var sr))
            _draft.AnimSwipeRubber = sr;
        _draft.AnimPulseEnabled = AnimPulseEnabledBox.IsChecked == true;
        _draft.AnimBreathEnabled = AnimBreathEnabledBox.IsChecked == true;

        if (Enum.TryParse<DateFormat>(SelectedTag(DateFormatBox), true, out var df))
            _draft.DateFormat = df;
        if (Enum.TryParse<ThemePreset>(SelectedTag(ThemePresetBox), true, out var tp))
            _draft.ThemePreset = tp;
        if (Enum.TryParse<WeatherLocationMode>(SelectedTag(WeatherLocationModeBox), true, out var wlm))
            _draft.WeatherLocationMode = wlm;
        _draft.WeatherLocationName = string.IsNullOrWhiteSpace(WeatherLocationNameBox.Text)
            ? "Москва"
            : WeatherLocationNameBox.Text.Trim();
        _draft.Latitude = (double)(LatitudeBox.Value ?? 55.75m);
        _draft.Longitude = (double)(LongitudeBox.Value ?? 37.62m);

        _draft.ColorCapsuleFill = AppSettings.NormalizeHex(ColorToHex(ColorFillPicker.Color), "#080808");
        _draft.ColorAccent = AppSettings.NormalizeHex(ColorToHex(ColorAccentPicker.Color), "#3D9CF0");
        _draft.ColorTextPrimary = AppSettings.NormalizeHex(ColorToHex(ColorTextPrimaryPicker.Color), "#FFFFFF");
        _draft.ColorTextSecondary = AppSettings.NormalizeHex(ColorToHex(ColorTextSecondaryPicker.Color), "#C8C8CC");
        _draft.IconPack = IconPackService.NormalizePack(SelectedTag(IconPackBox));
        _draft.FontFamily = AppSettings.NormalizeFontFamily(SelectedTag(FontFamilyBox));

        // Stock theme selected but UI diverged → Custom; else keep stock
        ThemePresets.AutodetectCustom(_draft);
        // If still stock, re-apply bundle so Apply is deterministic
        if (_draft.ThemePreset != ThemePreset.Custom)
            ThemePresets.Apply(_draft.ThemePreset, _draft);
    }

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        ReadUi();
        _onApply(_draft);
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        ReadUi();
        _onApply(_draft);
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
    private void OnDemoBattery(object? sender, RoutedEventArgs e)
    {
        // Apply current draft first so thresholds match, then fire demo on live overlay.
        ReadUi();
        _draft.CopyTo(_live);
        _live.Normalize();
        _live.Save();
        _onApply(_draft);
        _onDemoBattery?.Invoke();
    }

}
