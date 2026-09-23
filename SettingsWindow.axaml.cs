using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

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
    public SettingsWindow() : this(new AppSettings(), _ => { }) { }

    public SettingsWindow(AppSettings live, Action<AppSettings> onApply)
    {
        _live = live;
        _draft = new AppSettings();
        live.CopyTo(_draft);
        _onApply = onApply;
        // Use Avalonia-generated InitializeComponent so x:Name fields are wired.
        // A hand-written AvaloniaXamlLoader.Load(this) left named controls null → NRE.
        InitializeComponent();
        RestoreGeometry();
        LoadUi();
        WireVolumeLabels();
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
        IslandVisibleBox.IsChecked = _draft.IslandVisible;
        AllowDragBox.IsChecked = _draft.AllowDrag;
        WeatherEnabledBox.IsChecked = _draft.WeatherEnabled;
        SoundEnabledBox.IsChecked = _draft.SoundEnabled;
        OffsetXBox.Value = _draft.OffsetX;
        OffsetYBox.Value = _draft.OffsetY;
        OpacitySlider.Value = Math.Round(_draft.Opacity * 100);
        VolumeSlider.Value = Math.Round(_draft.SoundVolume * 100);
        VolNotifySlider.Value = Math.Round(_draft.SoundVolNotify * 100);
        VolExpandSlider.Value = Math.Round(_draft.SoundVolExpand * 100);
        VolCollapseSlider.Value = Math.Round(_draft.SoundVolCollapse * 100);
        VolSwipeSlider.Value = Math.Round(_draft.SoundVolSwipe * 100);
        VolErrorSlider.Value = Math.Round(_draft.SoundVolError * 100);
        VolHoverSlider.Value = Math.Round(_draft.SoundVolHover * 100);
        OpacityLabel.Text = $"{(int)OpacitySlider.Value}%";
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

    private void ReadUi()
    {
        _draft.IslandVisible = IslandVisibleBox.IsChecked == true;
        _draft.AllowDrag = AllowDragBox.IsChecked == true;
        _draft.WeatherEnabled = WeatherEnabledBox.IsChecked == true;
        _draft.SoundEnabled = SoundEnabledBox.IsChecked == true;
        _draft.OffsetX = (int)(OffsetXBox.Value ?? 0);
        _draft.OffsetY = (int)(OffsetYBox.Value ?? 0);
        _draft.Opacity = Math.Clamp(OpacitySlider.Value / 100.0, 0.35, 1.0);
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
}
