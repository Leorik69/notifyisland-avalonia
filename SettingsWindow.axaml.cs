using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Svg.Skia;
using Avalonia.Threading;

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
    private readonly Action<int>? _onStartTimer;
    private readonly Action? _onStartStopwatch;
    private bool _paletteWired;
    private bool _loadingUi;
    private readonly string? _initialSection;

    public SettingsWindow() : this(new AppSettings(), _ => { }) { }

    public SettingsWindow(AppSettings live, Action<AppSettings> onApply,
        Action<int>? onStartTimer = null, Action? onStartStopwatch = null,
        string? initialSection = null)
    {
        _live = live;
        _draft = new AppSettings();
        live.CopyTo(_draft);
        _onApply = onApply;
        _onStartTimer = onStartTimer;
        _onStartStopwatch = onStartStopwatch;
        InitializeComponent();
        _initialSection = initialSection;
        WireNav();
        SettingsSearchBox.TextChanged += OnSettingsSearchChanged;
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
        IslandWidthSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
            {
                // The label shows the resolved DIP width rather than the multiplier: a scale is
                // an implementation detail, "215 DIP" is the thing the user is actually choosing.
                IslandWidthLabel.Text =
                    IslandWidth.Describe(IslandWidthSlider.Value, _draft.WeatherEnabled);
                UpdatePreview();
            }
        };
        LowBatterySlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
                LowBatteryLabel.Text = $"{(int)LowBatterySlider.Value}";
        };
        TimerDefaultSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
                TimerDefaultLabel.Text = $"{(int)TimerDefaultSlider.Value}";
        };
        HoverDelaySlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
                HoverDelayLabel.Text = $"{(int)HoverDelaySlider.Value}";
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
        Width = Math.Clamp(_draft.SettingsWindowWidth, 640, 1400);
        Height = Math.Clamp(_draft.SettingsWindowHeight, 480, 1200);
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
        TimerEnabledBox.IsChecked = _draft.TimerEnabled;
        TimerStopwatchBox.IsChecked = _draft.TimerStopwatchMode;
        TimerDefaultSlider.Value = IslandTimerLogic.ClampPresetMinutes(_draft.TimerDefaultMinutes);
        if (TimerDefaultSlider.Value > 60) TimerDefaultSlider.Value = 60;
        TimerDefaultLabel.Text = $"{(int)TimerDefaultSlider.Value}";
        LowBatterySlider.Value = BatteryAlertLogic.ClampLowPercent(_draft.LowBatteryPercent);
        LowBatteryLabel.Text = $"{(int)LowBatterySlider.Value}";
        SoundEnabledBox.IsChecked = _draft.SoundEnabled;
        OffsetXBox.Value = _draft.OffsetX;
        OffsetYBox.Value = _draft.OffsetY;
        OpacitySlider.Value = Math.Round(_draft.Opacity * 100);
        FontSizeSlider.Value = Math.Clamp(_draft.FontSize, 10, 18);
        IslandWidthSlider.Value = IslandWidth.ClampScale(_draft.IslandWidthScale);
        IslandWidthLabel.Text =
            IslandWidth.Describe(IslandWidthSlider.Value, _draft.WeatherEnabled);
        VolumeSlider.Value = Math.Round(_draft.SoundVolume * 100);
        VolNotifySlider.Value = Math.Round(_draft.SoundVolNotify * 100);
        VolExpandSlider.Value = Math.Round(_draft.SoundVolExpand * 100);
        VolCollapseSlider.Value = Math.Round(_draft.SoundVolCollapse * 100);
        VolErrorSlider.Value = Math.Round(_draft.SoundVolError * 100);
        VolHoverSlider.Value = Math.Round(_draft.SoundVolHover * 100);
        OpacityLabel.Text = $"{(int)OpacitySlider.Value}%";
        FontSizeLabel.Text = $"{(int)FontSizeSlider.Value}";
        VolumeLabel.Text = $"{(int)VolumeSlider.Value}%";
        VolNotifyLabel.Text = $"{(int)VolNotifySlider.Value}%";
        VolExpandLabel.Text = $"{(int)VolExpandSlider.Value}%";
        VolCollapseLabel.Text = $"{(int)VolCollapseSlider.Value}%";
        VolErrorLabel.Text = $"{(int)VolErrorSlider.Value}%";
        VolHoverLabel.Text = $"{(int)VolHoverSlider.Value}%";
        SelectByTag(WeatherSideBox, _draft.WeatherSide.ToString());
        SelectByTag(EdgeBox, _draft.Edge.ToString());
        SelectByTag(OrientationBox, _draft.Orientation.ToString());
        SelectByTag(ZOrderBox, _draft.ZOrderMode.ToString());
        SelectByTag(SoundPackBox, _draft.SoundPack.ToString());
        ClipboardEnabledBox.IsChecked = _draft.ClipboardEnabled;
        SelectByTag(ClipboardMaxItemsBox, _draft.ClipboardMaxItems.ToString(CultureInfo.InvariantCulture));
        SelectByTag(ClipboardClickActionBox, _draft.ClipboardClickAction.ToString());
        SystemStatsEnabledBox.IsChecked = _draft.SystemStatsEnabled;
        SelectByTag(SystemStatsRefreshBox, _draft.SystemStatsRefreshMs.ToString(CultureInfo.InvariantCulture));
        SystemStatsHoverPeekBox.IsChecked = _draft.SystemStatsHoverPeek;
        SystemStatsAllInterfacesBox.IsChecked = _draft.SystemStatsAllInterfaces;
        // The «Выключить монитор» preset is not offered in the UI — SystemStatsEnabled is the one
        // off switch — so a settings.json carrying it (only reachable by hand-editing) migrates to
        // the Full preset here, once, instead of showing a selection the user cannot re-pick.
        if (_draft.StatsRowsPreset == StatsPreset.Off)
        {
            _draft.StatsRowsPreset = StatsPreset.Full;
            _draft.StatsRows = new List<StatsRow>(StatsLayout.FullRows);
        }
        SelectByTag(StatsRowsPresetBox, _draft.StatsRowsPreset.ToString());
        _statsRowEdit = new StatsRowEditState(_draft.StatsRows);
        _statsRowEditSeeded = true;
        RenderStatsRowsUi();
        AboutVersionText.Text = $"NotifyIsland {typeof(AppSettings).Assembly.GetName().Version?.ToString(3) ?? "1.12.0"}";
        AboutRuntimeText.Text = $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription} · Avalonia 11";
        AboutRepoText.Text = "https://github.com/Leorik69/notifyisland-avalonia";
        SettingsSearchBox.IsVisible = NavEntries.Length >= OverlayTokens.SettingsSearchMinSections;
        SelectByTag(AnimSpeedBox, _draft.AnimationSpeed.ToString());
        SelectByTag(AppearStyleBox, _draft.AppearStyle.ToString());
        SelectByTag(DismissStyleBox, _draft.DismissStyle.ToString());
        SelectByTag(AnimMorphInflateBox, _draft.AnimMorphInflate.ToString());
        SelectByTag(AnimMorphCollapseBox, _draft.AnimMorphCollapse.ToString());
        SelectByTag(AnimUnreadPulseBox, _draft.AnimUnreadPulse.ToString());
        SelectByTag(AnimHoverBox, _draft.AnimHover.ToString());
        SelectByTag(AnimClickPopBox, _draft.AnimClickPop.ToString());
        SelectByTag(AnimFirstAppearWobbleBox, _draft.AnimFirstAppearWobble.ToString());
        AnimPulseEnabledBox.IsChecked = _draft.AnimPulseEnabled;
        ReducedMotionBox.IsChecked = _draft.ReducedMotion;
        SelectByTag(IconPackBox, _draft.IconPack);
        SelectByTag(FontFamilyBox, _draft.FontFamily);
        SelectByTag(DateFormatBox, _draft.DateFormat.ToString());
        DigitalClockBox.IsChecked = _draft.DigitalClockEnabled;
        ShowClockSecondsBox.IsChecked = _draft.ShowClockSeconds;
        ShowSecondsStripBox.IsChecked = _draft.ShowSecondsStrip;
        HoverExpandBox.IsChecked = _draft.HoverExpandEnabled;
        HoverDelaySlider.Value = Math.Clamp(_draft.HoverExpandDelayMs, 0, 1000);
        HoverDelayLabel.Text = $"{(int)HoverDelaySlider.Value}";
        ClickPinBox.IsChecked = _draft.ClickPinEnabled;
        HideOnFullscreenBox.IsChecked = _draft.HideOnFullscreen;
        ClickThroughFullscreenBox.IsChecked = _draft.ClickThroughOnFullscreen;
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

    // -- Монитор: набор строк ------------------------------------------------------------
    // The editor is a plain ordered model (StatsRowEditState) plus two render passes: one that
    // draws the check/uncheck + up/down lines, one that draws the preview of the surface. Both
    // are rebuilt from scratch on every change — five rows is cheap, and rebuilding means the
    // tree can never drift from the model. Preview rows reuse the surface's own StatsRowView, so
    // what the user sees in Settings is the very control the island renders; only the sampled
    // values are missing, hence the «—» placeholder.

    private StatsRowEditState _statsRowEdit = new();

    /// <summary>The up/down buttons of the last rendered editor, per row, so focus can follow a move.</summary>
    private readonly Dictionary<StatsRow, (Avalonia.Controls.Button Up, Avalonia.Controls.Button Down)>
        _statsRowButtons = new();

    /// <summary>False until the custom editor has been seeded from the draft's row list once.</summary>
    private bool _statsRowEditSeeded;

    private StatsPreset SelectedStatsPreset =>
        Enum.TryParse<StatsPreset>(SelectedTag(StatsRowsPresetBox), true, out var p) ? p : StatsPreset.Full;

    private void OnStatsRowsPresetChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi) return;
        // Entering «Свой набор» must start from something sensible. The draft's row list has already
        // been reconciled with the preset that was active (Normalize() rewrites it), so seeding from
        // it hands the user that preset's rows as checked — Brief → Свой набор opens with CPU +
        // батарея on and память / сеть / дата parked below. Seeding happens only on the first
        // entry, so a look at another preset and back does not throw away edits made since.
        if (SelectedStatsPreset == StatsPreset.Custom && !_statsRowEditSeeded)
        {
            _statsRowEdit = new StatsRowEditState(_draft.StatsRows);
            _statsRowEditSeeded = true;
        }
        RenderStatsRowsUi();
    }

    private void RenderStatsRowsUi()
    {
        var custom = SelectedStatsPreset == StatsPreset.Custom;
        StatsRowsEditor.IsVisible = custom;
        StatsRowsEditorLabel.IsVisible = custom;
        StatsRowsEditorNote.IsVisible = custom;
        BuildStatsRowEditor();
        BuildStatsPreview();
    }

    private void BuildStatsRowEditor()
    {
        StatsRowsEditor.Children.Clear();
        _statsRowButtons.Clear();
        foreach (var item in _statsRowEdit.Items)
        {
            var row = item.Row;
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            line.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);

            var check = new Avalonia.Controls.CheckBox
            {
                Content = StatsLayout.NameFor(row),
                Tag = row.ToString(),
                IsChecked = item.IsVisible,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Opacity = item.IsVisible ? 1.0 : 0.45,
            };
            check.IsChecked = item.IsVisible;
            // Only the preview is rebuilt here: rebuilding the list under the user's cursor would
            // move keyboard focus off the checkbox they just pressed. Parked rows are dimmed in
            // place instead, so the model and the visuals stay in step without stealing focus.
            check.IsCheckedChanged += (_, _) =>
                OnStatsRowVisibilityChanged(row, check.IsChecked == true, check);
            Grid.SetColumn(check, 0);
            line.Children.Add(check);

            var up = MakeStatsRowButton("↑", "Поднять строку выше", enabled: _statsRowEdit.Items[0].Row != row);
            var down = MakeStatsRowButton("↓", "Опустить строку ниже",
                enabled: _statsRowEdit.Items[^1].Row != row);
            up.Click += (_, _) => MoveStatsRow(row, -1);
            down.Click += (_, _) => MoveStatsRow(row, +1);
            var buttons = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 4
            };
            buttons.Children.Add(up);
            buttons.Children.Add(down);
            _statsRowButtons[row] = (up, down);
            Grid.SetColumn(buttons, 2);
            line.Children.Add(buttons);

            StatsRowsEditor.Children.Add(line);
        }
    }

    private static Avalonia.Controls.Button MakeStatsRowButton(string glyph, string tip, bool enabled)
    {
        var btn = new Avalonia.Controls.Button
        {
            Content = glyph,
            Width = 34,
            Height = 26,
            Padding = new Avalonia.Thickness(0, 0, 0, 2),
            IsEnabled = enabled,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        Avalonia.Controls.ToolTip.SetTip(btn, tip);
        return btn;
    }

    private void OnStatsRowVisibilityChanged(StatsRow row, bool visible, Avalonia.Controls.CheckBox check)
    {
        if (_loadingUi) return;
        _statsRowEdit = _statsRowEdit.WithVisibility(row, visible);
        check.Opacity = visible ? 1.0 : 0.45;
        BuildStatsPreview();
    }

    /// <summary>Move a row one slot up/down. Focus is restored to the moved row's up button so a
    /// keyboard user can keep pressing it; the tree is rebuilt because the order itself changed.</summary>
    private void MoveStatsRow(StatsRow row, int delta)
    {
        if (_loadingUi) return;
        var moved = _statsRowEdit.Move(row, delta);
        if (ReferenceEquals(moved, _statsRowEdit)) return;   // already at that end
        _statsRowEdit = moved;
        RenderStatsRowsUi();
        FocusStatsRowButton(row, delta);
    }

    private void FocusStatsRowButton(StatsRow row, int delta)
    {
        if (_statsRowButtons.TryGetValue(row, out var pair))
            (delta < 0 ? pair.Up : pair.Down).Focus();
    }

    private void BuildStatsPreview()
    {
        StatsPreviewPanel.Children.Clear();
        var rows = _statsRowEdit.ResolveRows(SelectedStatsPreset);
        if (rows.Count == 0)
        {
            StatsPreviewPanel.Children.Add(new TextBlock
            {
                Text = "Панель не показывается",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#8A8A92")),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            });
            StatsPreviewPanel.Height = double.NaN;
            return;
        }

        foreach (var row in rows)
        {
            var view = new StatsRowView
            {
                IsCaptionRow = StatsLayout.IsCaptionRow(row),
                Label = StatsLayout.LabelFor(row),
                Value = "—",
                Caption = "—",
            };
            StatsPreviewPanel.Children.Add(view);
        }
        // Show the pill getting taller/shorter with the row count. The 12 DIP subtracted is the
        // real panel's Margin; the mock has none, so it uses the full inner content box instead.
        StatsPreviewPanel.Height = Math.Max(0, StatsLayout.StatsHeightFor(rows.Count) - 12);
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
        SelectByTag(AnimClickPopBox, s.AnimClickPop.ToString());
        SelectByTag(AnimFirstAppearWobbleBox, s.AnimFirstAppearWobble.ToString());
        SelectByTag(AppearStyleBox, s.AppearStyle.ToString());
        SelectByTag(DismissStyleBox, s.DismissStyle.ToString());
        SelectByTag(DateFormatBox, s.DateFormat.ToString());
        DigitalClockBox.IsChecked = s.DigitalClockEnabled;
        ShowClockSecondsBox.IsChecked = s.ShowClockSeconds;
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
        _draft.TimerEnabled = TimerEnabledBox.IsChecked == true;
        _draft.TimerStopwatchMode = TimerStopwatchBox.IsChecked == true;
        _draft.TimerDefaultMinutes = IslandTimerLogic.ClampPresetMinutes((int)TimerDefaultSlider.Value);
        _draft.LowBatteryPercent = BatteryAlertLogic.ClampLowPercent((int)LowBatterySlider.Value);
        _draft.SoundEnabled = SoundEnabledBox.IsChecked == true;
        _draft.OffsetX = (int)(OffsetXBox.Value ?? 0);
        _draft.OffsetY = (int)(OffsetYBox.Value ?? 0);
        _draft.Opacity = Math.Clamp(OpacitySlider.Value / 100.0, 0.35, 1.0);
        _draft.FontSize = Math.Clamp(FontSizeSlider.Value, 10, 18);
        _draft.IslandWidthScale = IslandWidth.SnapToStep(IslandWidthSlider.Value);
        _draft.SoundVolume = Math.Clamp(VolumeSlider.Value / 100.0, 0.0, 1.0);
        _draft.SoundVolNotify = Math.Clamp(VolNotifySlider.Value / 100.0, 0.0, 1.0);
        _draft.SoundVolExpand = Math.Clamp(VolExpandSlider.Value / 100.0, 0.0, 1.0);
        _draft.SoundVolCollapse = Math.Clamp(VolCollapseSlider.Value / 100.0, 0.0, 1.0);
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
        _draft.ClipboardEnabled = ClipboardEnabledBox.IsChecked == true;
        if (int.TryParse(SelectedTag(ClipboardMaxItemsBox), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cm))
            _draft.ClipboardMaxItems = cm;
        if (Enum.TryParse<ClipboardClickAction>(SelectedTag(ClipboardClickActionBox), true, out var ca))
            _draft.ClipboardClickAction = ca;
        _draft.SystemStatsEnabled = SystemStatsEnabledBox.IsChecked == true;
        if (int.TryParse(SelectedTag(SystemStatsRefreshBox), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var statsMs))
            _draft.SystemStatsRefreshMs = statsMs;
        _draft.SystemStatsHoverPeek = SystemStatsHoverPeekBox.IsChecked == true;
        _draft.SystemStatsAllInterfaces = SystemStatsAllInterfacesBox.IsChecked == true;
        // Preset first, then the row order — Normalize() reconciles the two (a non-custom preset
        // overwrites the list), so nothing here tries to keep them consistent by hand.
        if (Enum.TryParse<StatsPreset>(SelectedTag(StatsRowsPresetBox), true, out var statsPreset))
            _draft.StatsRowsPreset = statsPreset;
        _draft.StatsRows = new List<StatsRow>(_statsRowEdit.ToCustomRows());
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
        if (Enum.TryParse<AnimationSpeed>(SelectedTag(AnimHoverBox), true, out var hv))
            _draft.AnimHover = hv;
        if (Enum.TryParse<AnimationSpeed>(SelectedTag(AnimClickPopBox), true, out var cp))
            _draft.AnimClickPop = cp;
        if (Enum.TryParse<AnimationSpeed>(SelectedTag(AnimFirstAppearWobbleBox), true, out var fw))
            _draft.AnimFirstAppearWobble = fw;
        _draft.AnimPulseEnabled = AnimPulseEnabledBox.IsChecked == true;
        _draft.ReducedMotion = ReducedMotionBox.IsChecked == true;

        if (Enum.TryParse<DateFormat>(SelectedTag(DateFormatBox), true, out var df))
            _draft.DateFormat = df;
        _draft.DigitalClockEnabled = DigitalClockBox.IsChecked == true;
        _draft.ShowClockSeconds = ShowClockSecondsBox.IsChecked == true;
        _draft.ShowSecondsStrip = ShowSecondsStripBox.IsChecked == true;
        _draft.HoverExpandEnabled = HoverExpandBox.IsChecked == true;
        _draft.HoverExpandDelayMs = (int)Math.Clamp(HoverDelaySlider.Value, 0, 1000);
        _draft.HoverCollapseGraceMs = OverlayTokens.HoverCollapseGraceMs;
        _draft.ClickPinEnabled = ClickPinBox.IsChecked == true;
        _draft.HideOnFullscreen = HideOnFullscreenBox.IsChecked == true;
        _draft.ClickThroughOnFullscreen = ClickThroughFullscreenBox.IsChecked == true;
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

    private void OnStartTimer1(object? sender, RoutedEventArgs e) => _onStartTimer?.Invoke(1);
    private void OnStartTimer5(object? sender, RoutedEventArgs e) => _onStartTimer?.Invoke(5);
    private void OnStartTimer10(object? sender, RoutedEventArgs e) => _onStartTimer?.Invoke(10);
    private void OnStartTimer25(object? sender, RoutedEventArgs e) => _onStartTimer?.Invoke(25);
    private void OnStartTimerDefault(object? sender, RoutedEventArgs e)
    {
        if (TimerStopwatchBox.IsChecked == true)
            _onStartStopwatch?.Invoke();
        else
            _onStartTimer?.Invoke(IslandTimerLogic.ClampPresetMinutes((int)TimerDefaultSlider.Value));
    }

    private static readonly (string Id, string Icon)[] NavEntries =
    [
        ("island", "layout-dashboard"),
        ("weather", "cloud-sun"),
        ("placement", "move"),
        ("theme", "palette"),
        ("media", "music"),
        ("look", "type"),
        ("anim", "sparkles"),
        ("sound", "volume-2"),
        ("icons", "shapes"),
        ("clipboard", "clipboard"),
        ("system", "activity"),
        ("about", "info"),
    ];

    private void WireNav()
    {
        var target = FindNavItem(_initialSection);
        NavList.SelectedItem = target;
        if (NavList.SelectedIndex < 0 && NavList.ItemCount > 0)
            NavList.SelectedIndex = 0;
        ApplyNavSelection();
        TintSelectedNavIcon();
    }

    /// <summary>
    /// Selects a sidebar section by its nav tag (e.g. "system" for «Монитор»).
    /// Unknown tags leave the current selection untouched.
    /// </summary>
    public void SelectSection(string tag)
    {
        if (FindNavItem(tag) is not { } item) return;
        NavList.SelectedItem = item;
        ApplyNavSelection();
        TintSelectedNavIcon();
    }

    private ListBoxItem? FindNavItem(string? tag) =>
        string.IsNullOrEmpty(tag)
            ? null
            : NavList.Items.OfType<ListBoxItem>().FirstOrDefault(
                i => string.Equals(i.Tag?.ToString(), tag, StringComparison.Ordinal));

    private void OnNavSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        ApplyNavSelection();
        TintSelectedNavIcon();
    }

    private void ApplyNavSelection()
    {
        var tag = (NavList.SelectedItem as ListBoxItem)?.Tag?.ToString() ?? "island";
        foreach (var (id, _) in NavEntries)
        {
            if (this.FindControl<ScrollViewer>($"Panel_{id}") is { } panel)
                panel.IsVisible = string.Equals(id, tag, StringComparison.Ordinal);
        }
    }

    private void TintSelectedNavIcon()
    {
        var tag = (NavList.SelectedItem as ListBoxItem)?.Tag?.ToString();
        foreach (var (id, icon) in NavEntries)
        {
            if (this.FindControl<Avalonia.Controls.Image>($"NavIcon_{id}") is not { } img) continue;
            var hex = string.Equals(id, tag, StringComparison.Ordinal) ? "#5CB6FF" : "#C8C8CC";
            img.Source = LoadNavSvg(icon, hex);
        }
    }

    private static SvgImage? LoadNavSvg(string iconFile, string hex)
    {
        try
        {
            var path = ResolveNavSvgPath(iconFile);
            if (path is null) return null;
            var xml = File.ReadAllText(path);
            xml = xml.Replace("currentColor", hex, StringComparison.OrdinalIgnoreCase);
            var loaded = SvgSource.LoadFromSvg(xml);
            if (loaded is null) return null;
            return new SvgImage { Source = loaded };
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveNavSvgPath(string iconFile)
    {
        var name = iconFile + ".svg";
        var candidates = new[]
        {
            System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "Lucide", name),
            System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", "Icons", "Lucide", name)),
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;
        return null;
    }

    private void OnSettingsSearchChanged(object? sender, TextChangedEventArgs e)
    {
        var q = SettingsSearchBox.Text?.Trim() ?? "";
        foreach (var item in NavList.Items.OfType<ListBoxItem>())
        {
            string? label = null;
            if (item.Content is StackPanel sp)
                label = sp.Children.OfType<TextBlock>().FirstOrDefault()?.Text;
            item.IsVisible = q.Length == 0
                || (label?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
        }
    }

    private void OnExportSettings(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var window = GetTopLevel(this);
        if (window is null) return;
        var path = new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            SuggestedFileName = "notifyisland-settings.json",
            DefaultExtension = "json"
        };
        window.StorageProvider.SaveFilePickerAsync(path).ContinueWith(t =>
        {
            if (t.Status != TaskStatus.RanToCompletion || t.Result is null) return;
            using var stream = t.Result.OpenWriteAsync().GetAwaiter().GetResult();
            using var writer = new StreamWriter(stream);
            writer.Write(System.Text.Json.JsonSerializer.Serialize(_draft,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        });
    }

    private void OnImportSettings(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var window = GetTopLevel(this);
        if (window is null) return;
        window.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("JSON")
                { Patterns = new[] { "*.json" } } }
        }).ContinueWith(t =>
        {
            if (t.Status != TaskStatus.RanToCompletion || t.Result is null || t.Result.Count == 0) return;
            using var stream = t.Result[0].OpenReadAsync().GetAwaiter().GetResult();
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            try
            {
                var loaded = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded is null) return;
                loaded.Normalize();
                Dispatcher.UIThread.Post(() =>
                {
                    loaded.CopyTo(_draft);
                    LoadUi();
                });
            }
            catch (Exception ex)
            {
                AppLog.Warn("settings import failed", ex);
            }
        });
    }

}
