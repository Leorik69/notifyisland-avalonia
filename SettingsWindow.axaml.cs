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
        _animPreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _animPreviewTimer.Tick += OnAnimPreviewTick;
        _initialSection = initialSection;
        WireNav();
        SettingsSearchBox.TextChanged += OnSettingsSearchChanged;
        RestoreGeometry();
        LoadUi();
        WireVolumeLabels();
        WirePalettePreview();
        WireLiveSync();
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

    private void OnIconPackChanged(object? sender, SelectionChangedEventArgs e)
    {
        RefreshIconsPreview();
    }

    /// <summary>
    /// Превью выбранного пакета иконок: два смысловых блока (интерфейсные глифы и погодные)
    /// в адаптивной сетке WrapPanel.
    /// <para>
    /// Раньше панель была пустым <c>Panel</c>, а позиция каждой иконки считалась вручную:
    /// <c>startX += size + gap + estimatedLabelWidth</c>. Это неверно по трём причинам сразу —
    /// ширина подписи оценивалась на глаз (длинное «Уведомление» наезжало на соседнюю
    /// иконку), ряд не переносился и на узком окне уезжал за границу карточки, а
    /// «прозрачный» Panel вообще ничего не ограничивал. WrapPanel переносит сам, метки
    /// получают свою естественную ширину, и всё остаётся внутри карточки при любой ширине.
    /// </para>
    /// <para>
    /// Fallback не дублируется здесь: <see cref="IconPackService.Create"/> сам отдаёт
    /// IslandIcons для ключа, которого в пакете нет (в том числе для Meteocons и
    /// не-погодных ключей), поэтому список всегда полный и честный.
    /// </para>
    /// </summary>
    private void RefreshIconsPreview()
    {
        try
        {
            var panel = IconsPreviewPanel;
            if (panel is null) return;
            panel.Children.Clear();

            var pack = IconPackService.NormalizePack(SelectedTag(IconPackBox));
            var stroke = new SolidColorBrush(ColorTextSecondaryPicker.Color);
            var muted = new SolidColorBrush(Color.Parse("#8A8A92"));

            var groups = new (string Title, string Hint, (string Key, string Label)[] Keys)[]
            {
                ("Интерфейс", "часы, питание, уведомления, медиа, таймер, ошибка", new[]
                {
                    ("clock", "Часы"),
                    ("battery", "Батарея"),
                    ("notify", "Уведомление"),
                    ("media", "Медиа"),
                    ("timer", "Таймер"),
                    ("error", "Ошибка"),
                }),
                ("Погода", "у Meteocons — свои глифы, у остальных пакетов — fallback", new[]
                {
                    ("weather-clear", "Ясно"),
                    ("weather-rain", "Дождь"),
                }),
            };

            var missing = 0;
            foreach (var (title, hint, keys) in groups)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = title,
                    FontSize = 10.5,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(Color.Parse("#C8C8CC")),
                    Margin = new Thickness(0, panel.Children.Count > 0 ? 12 : 0, 0, 0),
                });
                panel.Children.Add(new TextBlock
                {
                    Text = hint,
                    FontSize = 10,
                    Foreground = muted,
                    Margin = new Thickness(0, 2, 0, 8),
                });

                var row = new WrapPanel();
                foreach (var (key, label) in keys)
                {
                    var icon = IconPackService.Create(pack, key, 22, stroke);
                    if (icon is null) { missing++; continue; }
                    icon.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
                    row.Children.Add(IconCell(icon, label, muted));
                }
                panel.Children.Add(row);
            }

            if (missing > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"{missing} глиф(ов) не удалось загрузить — показан встроенный IslandIcons.",
                    FontSize = 10,
                    Foreground = muted,
                    Margin = new Thickness(0, 8, 0, 0),
                });
            }
        }
        catch
        {
            // design-time / partial init
        }
    }

    /// <summary>One preview cell: the glyph on top, its own name under it, both at natural width.</summary>
    private static Border IconCell(Avalonia.Controls.Control icon, string label, IBrush muted)
    {
        var cell = new StackPanel
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            MinWidth = 76,
        };
        cell.Children.Add(icon);
        cell.Children.Add(new TextBlock
        {
            Text = label,
            Classes = { "iconCellLabel" },
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });

        var host = new Border { Classes = { "iconCell" } };
        host.Child = cell;
        return host;
    }

    /// <summary>Wires every user-control change to live-apply without requiring the Apply/OK buttons.
    /// Changes are written to _live immediately (saved to disk by _onApply → _live.Save).
    /// Reset to Defaults uses a fresh AppSettings() as the source instead.</summary>
    private void WireLiveSync()
    {
        // For CheckBox / Slider / NumericUpDown / TextBox: use PropertyChanged on the AvaloniaProperty.
        void Wire<T>(T ctrl, AvaloniaProperty prop) where T : AvaloniaObject
            => ctrl.PropertyChanged += (_, e) => { if (e.Property == prop) ApplyLive(); };

        // For ComboBox / ListBox: SelectionChanged fires reliably when SelectedItem changes.
        void WireSel<T>(T ctrl) where T : Avalonia.Controls.Primitives.SelectingItemsControl
            => ctrl.SelectionChanged += (_, _) => ApplyLive();

        // Island
        Wire(IslandVisibleBox, Avalonia.Controls.CheckBox.IsCheckedProperty);
        WireSel(DateFormatBox);
        Wire(DigitalClockBox, Avalonia.Controls.CheckBox.IsCheckedProperty);
        Wire(ShowClockSecondsBox, Avalonia.Controls.CheckBox.IsCheckedProperty);
        Wire(ShowSecondsStripBox, Avalonia.Controls.CheckBox.IsCheckedProperty);
        Wire(HoverExpandBox, Avalonia.Controls.CheckBox.IsCheckedProperty);
        Wire(HoverDelaySlider, Slider.ValueProperty);
        Wire(ClickPinBox, Avalonia.Controls.CheckBox.IsCheckedProperty);
        Wire(HideOnFullscreenBox, Avalonia.Controls.CheckBox.IsCheckedProperty);
        Wire(ClickThroughFullscreenBox, Avalonia.Controls.CheckBox.IsCheckedProperty);

        // Appearance
        Wire(OpacitySlider, Slider.ValueProperty);
        Wire(FontSizeSlider, Slider.ValueProperty);
        Wire(IslandWidthSlider, Slider.ValueProperty);
        WireSel(ZOrderBox);
        WireSel(FontFamilyBox);
        Wire(ColorFillPicker, Avalonia.Controls.ColorPicker.ColorProperty);
        Wire(ColorAccentPicker, Avalonia.Controls.ColorPicker.ColorProperty);
        Wire(ColorTextPrimaryPicker, Avalonia.Controls.ColorPicker.ColorProperty);
        Wire(ColorTextSecondaryPicker, Avalonia.Controls.ColorPicker.ColorProperty);

        // Behavior
        Wire(ShowNowPlayingBox, Avalonia.Controls.CheckBox.IsCheckedProperty);
        Wire(ShowBatteryAlertsBox, Avalonia.Controls.CheckBox.IsCheckedProperty);
        Wire(ShowBatteryInCollapsedBox, Avalonia.Controls.CheckBox.IsCheckedProperty);
        Wire(TimerEnabledBox, Avalonia.Controls.CheckBox.IsCheckedProperty);
        Wire(TimerStopwatchBox, Avalonia.Controls.CheckBox.IsCheckedProperty);
        Wire(TimerDefaultSlider, Slider.ValueProperty);
        Wire(LowBatterySlider, Slider.ValueProperty);
        Wire(ClipboardEnabledBox, Avalonia.Controls.CheckBox.IsCheckedProperty);
        WireSel(ClipboardMaxItemsBox);
        WireSel(ClipboardClickActionBox);

        // Monitor
        WireSel(SystemStatsRefreshBox);
        Wire(SystemStatsHoverPeekBox, Avalonia.Controls.CheckBox.IsCheckedProperty);
        Wire(SystemStatsAllInterfacesBox, Avalonia.Controls.CheckBox.IsCheckedProperty);
        WireSel(StatsRowsPresetBox);

        // Animation
        WireSel(AnimSpeedBox);
        WireSel(AppearStyleBox);
        WireSel(DismissStyleBox);
        WireSel(AnimMorphInflateBox);
        WireSel(AnimMorphCollapseBox);
        WireSel(AnimUnreadPulseBox);
        WireSel(AnimHoverBox);
        WireSel(AnimClickPopBox);
        WireSel(AnimFirstAppearWobbleBox);
        Wire(AnimPulseEnabledBox, Avalonia.Controls.CheckBox.IsCheckedProperty);
        Wire(ReducedMotionBox, Avalonia.Controls.CheckBox.IsCheckedProperty);

        // Sound
        Wire(SoundEnabledBox, Avalonia.Controls.CheckBox.IsCheckedProperty);
        WireSel(SoundPackBox);
        Wire(VolumeSlider, Slider.ValueProperty);
        Wire(VolNotifySlider, Slider.ValueProperty);
        Wire(VolExpandSlider, Slider.ValueProperty);
        Wire(VolCollapseSlider, Slider.ValueProperty);
        Wire(VolErrorSlider, Slider.ValueProperty);
        Wire(VolHoverSlider, Slider.ValueProperty);

        // Weather
        Wire(WeatherEnabledBox, Avalonia.Controls.CheckBox.IsCheckedProperty);
        WireSel(WeatherSideBox);
        WireSel(WeatherLocationModeBox);
        Wire(WeatherLocationNameBox, Avalonia.Controls.TextBox.TextProperty);
        Wire(LatitudeBox, Avalonia.Controls.NumericUpDown.ValueProperty);
        Wire(LongitudeBox, Avalonia.Controls.NumericUpDown.ValueProperty);

        // Icons
        WireSel(IconPackBox);

        // Placement
        WireSel(EdgeBox);
        Wire(OffsetXBox, Avalonia.Controls.NumericUpDown.ValueProperty);
        Wire(OffsetYBox, Avalonia.Controls.NumericUpDown.ValueProperty);
        WireSel(OrientationBox);

        // Theme
        WireSel(ThemePresetBox);

        // Мастер-переключатели: от них зависит читаемость зависимых полей, поэтому они
        // обновляют приглушение, а не только применяют значение.
        void WireDeps(AvaloniaObject ctrl, AvaloniaProperty prop) =>
            ctrl.PropertyChanged += (_, e) => { if (e.Property == prop) UpdateDependencyStates(); };
        WireDeps(WeatherEnabledBox, Avalonia.Controls.CheckBox.IsCheckedProperty);
        WireDeps(TimerEnabledBox, Avalonia.Controls.CheckBox.IsCheckedProperty);
        WireDeps(SoundEnabledBox, Avalonia.Controls.CheckBox.IsCheckedProperty);
        WireDeps(ReducedMotionBox, Avalonia.Controls.CheckBox.IsCheckedProperty);
        AnimSpeedBox.SelectionChanged += (_, _) => { UpdateDependencyStates(); RefreshAnimPreviewHint(); };
        AppearStyleBox.SelectionChanged += (_, _) => RefreshAnimPreviewHint();
        DismissStyleBox.SelectionChanged += (_, _) => RefreshAnimPreviewHint();
    }

    /// <summary>
    /// Перерисовывает превью капсулы и всё, что в окне зависит от палитры.
    /// <para>
    /// Превью — это уменьшенная копия настоящей свёрнутой капсулы, а не абстрактный образец
    /// цвета: та же высота 30 DIP, тот же радиус (полкруга), тот же хайрлайн #16FFFFFF, тот
    /// же шрифт, та же цифра часов, та же вторичная дата и та же точка непрочитанного. Если
    /// превью расходится с островком, пользователь правит настройку вслепую.
    /// </para>
    /// <para>
    /// Порядок элементов повторяет ApplyWeatherSide: WeatherSide=Left ставит погоду перед
    /// часами, Right — после. Цифровые часы переиспользуют тот же DigitalClockView, что и
    /// островок, поэтому глифы в превью настоящие, а не имитация.
    /// </para>
    /// </summary>
    private void UpdatePreview()
    {
        try
        {
            var fill = ColorFillPicker.Color;
            var accent = ColorAccentPicker.Color;
            // Превью повторяет ровно тот же safeguard, что и островок: если пользователь подобрал
            // цвет текста в цвет своей же заливки, капсула обязана остаться читаемой — и превью
            // не должно показывать то, чего на островке не будет.
            var primaryHex = ThemePresets.GuardedPrimaryInk(ColorToHex(fill), ColorToHex(ColorTextPrimaryPicker.Color));
            var secondaryHex = ThemePresets.GuardedSecondaryInk(
                ColorToHex(fill), ColorToHex(ColorTextSecondaryPicker.Color), primaryHex);
            var primary = Color.Parse(primaryHex);
            var secondary = Color.Parse(secondaryHex);
            // Материал темы (прозрачность стенки и вес волосяной линии) — тот же, что применит
            // оверлей. Раньше превью показывало только заливку, а граница оставалась белой
            // константой из XAML, то есть на светлой капсуле превью врало.
            var material = ThemePresets.MaterialFor(PreviewTheme());
            var alpha = Math.Clamp(OpacitySlider.Value / 100.0 * material.FillAlphaScale, 0.30, 1.0);

            PreviewPill.Background = new SolidColorBrush(WithAlpha(fill, alpha));
            var previewBorder = new SolidColorBrush(WithAlpha(primary, material.BorderAlpha));
            PreviewPill.BorderBrush = previewBorder;
            AnimPreviewCapsule.Background = new SolidColorBrush(WithAlpha(fill, alpha));
            AnimPreviewCapsule.BorderBrush = previewBorder;
            PreviewDot.Background = new SolidColorBrush(accent);
            PreviewPrimary.Foreground = new SolidColorBrush(primary);
            PreviewSecondary.Foreground = new SolidColorBrush(secondary);
            PreviewWeather.Foreground = new SolidColorBrush(secondary);

            var font = IslandFonts.Resolve(SelectedTag(FontFamilyBox));
            var fs = Math.Clamp(FontSizeSlider.Value, 10, 18);
            PreviewPrimary.FontFamily = font;
            PreviewPrimary.FontSize = fs + 1;                 // часы на капсуле крупнее базового
            PreviewSecondary.FontFamily = font;
            PreviewSecondary.FontSize = Math.Max(10, fs - 2);
            PreviewWeather.FontFamily = font;
            PreviewWeather.FontSize = Math.Max(10, fs - 2);

            // Дата — по выбранному формату, ровно как её рисует DateFormatHelper.
            var dateTag = SelectedTag(DateFormatBox);
            PreviewSecondary.Text = DatePreviewFor(dateTag);
            PreviewSecondary.IsVisible = !string.IsNullOrEmpty(PreviewSecondary.Text);

            // Погода слева или справа от часов.
            var weatherOn = WeatherEnabledBox.IsChecked == true;
            PreviewWeather.IsVisible = weatherOn;
            var weatherLeft = string.Equals(SelectedTag(WeatherSideBox), "Left", StringComparison.OrdinalIgnoreCase);
            LayoutPreviewRow(weatherOn, weatherLeft);

            // Цифровые часы: тот же построитель глифов, что и на островке.
            var digitalOn = DigitalClockBox.IsChecked == true;
            var digitalOk = digitalOn && DigitalClockView.Apply(
                PreviewDigitalRow, "14:32", Math.Clamp(fs + 3, 12, 22), new SolidColorBrush(primary), true);
            PreviewDigitalRow.IsVisible = digitalOk;
            PreviewPrimary.IsVisible = !digitalOk;

            // Ширина — та же формула, что у островка, поэтому ползунок длины виден здесь.
            var weatherForWidth = weatherOn;
            PreviewPill.Width = Math.Max(
                120,
                Math.Round(IslandWidth.CollapsedLongAxis(IslandWidthSlider.Value, weatherForWidth)));

            ApplyThemeToChrome(accent);
            UpdateSwatchSelection();
            BuildStatsPreview();
        }
        catch
        {
            // design-time / partial init
        }
    }

    /// <summary>
    /// Which theme's material the preview should render with.
    /// <para>
    /// The combo still says "AppleQuiet" for a moment after the user hand-tunes a swatch — the
    /// switch to Custom only happens when the draft is saved. Painting AppleQuiet's lighter wall
    /// for a palette the user already changed is exactly the kind of lie a preview must not tell,
    /// so the stock material is used only while the capsule fill is still that preset's own fill.
    /// Everything else is Custom, which is the neutral rule.
    /// </para>
    /// </summary>
    private ThemePreset PreviewTheme()
    {
        if (!Enum.TryParse<ThemePreset>(SelectedTag(ThemePresetBox), true, out var sel) || sel == ThemePreset.Custom)
            return ThemePreset.Custom;
        var stock = new AppSettings();
        ThemePresets.Apply(sel, stock);
        var same = string.Equals(
            AppSettings.NormalizeHex(ColorToHex(ColorFillPicker.Color), ""),
            AppSettings.NormalizeHex(stock.ColorCapsuleFill, ""),
            StringComparison.OrdinalIgnoreCase);
        return same ? sel : ThemePreset.Custom;
    }

    /// <summary>
    /// Переносит акцент палитры на элементы окна, у которых нет прямого доступа к настройке:
    /// заливку бегунков, подложку активного раздела и главную кнопку.
    /// <para>
    /// Раньше всё это было зашито константами (#243447, синий Fluent-бегунок), поэтому
    /// Settings выдавали себя за чужое приложение: островок менял акцент на зелёный, а
    /// боковая навигация и ползунки оставались синими.
    /// </para>
    /// </summary>
    private void ApplyThemeToChrome(Color accent)
    {
        // Активный раздел — мягкая заливка акцентом, а не рамка: рамка сделала бы весь
        // список «подсвеченным» и съела бы разницу с hover. Кисть живёт в ресурсах окна,
        // поэтому её цвет — это ровно ColorAccent, и навигация едет вместе с палитрой.
        if (Resources["NavActiveBrush"] is SolidColorBrush navBrush)
            navBrush.Color = WithAlpha(accent, 0.20);

        // Кнопка «Закрыть» — единственная с заливкой во всём окне, поэтому её акцент
        // обязан совпадать с акцентом островка.
        CloseBtn.Background = new SolidColorBrush(accent);
        CloseBtn.Foreground = new SolidColorBrush(ReadabilityOn(accent));
    }

    /// <summary>
    /// Контрастный цвет текста для заливки: тёмный на светлом акценте, светлый на тёмном.
    /// Без этого светло-голубой акцент давал бы на кнопке белый текст — читаемый, но бледный.
    /// </summary>
    private static Color ReadabilityOn(Color c) =>
        (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 150 ? Color.Parse("#0A0A0C") : Colors.White;

    private static Color WithAlpha(Color c, double a) =>
        Color.FromArgb((byte)Math.Clamp(a * 255, 0, 255), c.R, c.G, c.B);

    /// <summary>Помечает тот свотч, который соответствует текущему цвету — выбор виден без подписи.</summary>
    private void UpdateSwatchSelection()
    {
        MarkActive(SwatchFillRow, ColorToHex(ColorFillPicker.Color));
        MarkActive(SwatchAccentRow, ColorToHex(ColorAccentPicker.Color));
        MarkActive(SwatchPrimaryRow, ColorToHex(ColorTextPrimaryPicker.Color));
        MarkActive(SwatchSecondaryRow, ColorToHex(ColorTextSecondaryPicker.Color));
    }

    private static void MarkActive(Avalonia.Controls.Panel row, string hex)
    {
        foreach (var border in row.Children.OfType<Border>())
        {
            var on = border.Tag?.ToString()?.EndsWith("|" + hex, StringComparison.OrdinalIgnoreCase) == true;
            border.Classes.Set("on", on);
        }
    }

    /// <summary>Демонстрационная дата для превью: формат берётся из настройки, значение — фиксированное.</summary>
    private static string DatePreviewFor(string? tag) => tag switch
    {
        null or "Off" => "",
        "DayMonth" => "24 сен",
        "WeekdayShort" => "ср",
        "WeekdayDay" => "ср 24",
        "Numeric" => "24.09",
        "FullShort" => "ср, 24 сен",
        _ => "24 сен",
    };

    /// <summary>
    /// Пересобирает порядок элементов строки превью. Погода едет в сторону, выбранную в
    /// настройках; точка непрочитанного всегда последняя, как на островке.
    /// </summary>
    private void LayoutPreviewRow(bool weatherOn, bool weatherLeft)
    {
        var row = PreviewRow;
        row.Children.Clear();
        if (weatherOn && weatherLeft) row.Children.Add(PreviewWeather);
        row.Children.Add(PreviewDigitalRow);
        row.Children.Add(PreviewPrimary);
        row.Children.Add(PreviewSecondary);
        if (weatherOn && !weatherLeft) row.Children.Add(PreviewWeather);
        row.Children.Add(PreviewDot);
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
        _animPreviewTimer.Stop();
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
        RefreshIconsPreview();
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
        UpdateDependencyStates();
        RefreshAnimPreviewHint();
        ResetAnimPreview();
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

    /// <summary>
    /// Превью панели монитора: те же <see cref="StatsRowView"/>, что рисует островок, в том
    /// же порядке и с теми же демо-значениями.
    /// <para>
    /// Раньше все строки показывали «—». Это отвечало на вопрос «какие строки включены», но
    /// не отвечало на вопрос «как это будет выглядеть»: без значения не видно ни ширины
    /// панели, ни того, вписывается ли длинный заголовок трека в строку, ни где встанет
    /// разделитель перед датой. Значения демонстрационные и НЕ подключены к SystemMonitor —
    /// превью остаётся статичным представлением, как и просит спецификация.
    /// </para>
    /// </summary>
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

        var value = new SolidColorBrush(ColorTextPrimaryPicker.Color);
        var font = IslandFonts.Resolve(SelectedTag(FontFamilyBox));
        foreach (var row in rows)
        {
            var view = new StatsRowView
            {
                IsCaptionRow = StatsLayout.IsCaptionRow(row),
                Label = StatsLayout.LabelFor(row),
                Value = "—",
                Caption = "—",
                // FontFamily наследуется вниз по дереву, поэтому строка целиком берёт шрифт
                // островка одной строкой — размеры оставлены те же, что у настоящей панели.
                FontFamily = font,
            };
            if (StatsLayout.IsCaptionRow(row))
            {
                view.Caption = DatePreviewCaption();
            }
            else if (StatsLayout.HasActions(row))
            {
                view.SetStatus(DemoStatusRow(row));
            }
            else
            {
                view.Value = DemoMetricFor(row);
                view.ValueBrush = value;
            }
            StatsPreviewPanel.Children.Add(view);
        }
        // Показываем капсулу растущей и уменьшающейся вместе с числом строк. 12 DIP — реальный
        // Margin панели; у макета его нет, поэтому он использует весь внутренний бокс.
        StatsPreviewPanel.Height = Math.Max(0, StatsLayout.StatsHeightFor(rows.Count) - 12);
    }

    private static string DatePreviewCaption() => "среда, 30 сентября";

    private static string DemoMetricFor(StatsRow row) => row switch
    {
        StatsRow.Cpu => "13 %",
        StatsRow.Memory => "26,3 / 34 ГБ",
        StatsRow.Battery => "100 %",
        StatsRow.Network => "↓ 0,0  ↑ 0,1",
        _ => "—",
    };

    private static StatusRowModel DemoStatusRow(StatsRow row) => row switch
    {
        StatsRow.Media => new StatusRowModel
        {
            Kind = StatusRowKind.Media,
            Label = "Плеер",
            Value = "Гипербола",
            Detail = "Монстрumente",
            Progress = 0.42,
            Playing = true,
            Active = true,
        },
        StatsRow.Timer => new StatusRowModel
        {
            Kind = StatusRowKind.Timer,
            Label = "Таймер",
            Value = "04:32",
            Detail = "Осталось",
            Progress = 0.91,
            Playing = true,
            Active = true,
        },
        _ => new StatusRowModel(),
    };

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
        RefreshIconsPreview();
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

    private void OnApply(object? sender, RoutedEventArgs e) => ApplyLive();

    private void OnOk(object? sender, RoutedEventArgs e) => Close();

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        AppSettings.Load().CopyTo(_live); // reload from disk — undoes all live changes made during this session
        Close();
    }

    /// <summary>Applies the current control state to live settings immediately (no OK/Apply button needed).
    /// Reset to Defaults uses a fresh AppSettings() as the source instead.</summary>
    private void ApplyLive()
    {
        ReadUi();
        _onApply(_draft);
    }

    /// <summary>Resets all settings to factory defaults and applies them live immediately.</summary>
    private void OnResetToDefaults(object? sender, RoutedEventArgs e)
    {
        var defaults = new AppSettings();
        defaults.CopyTo(_draft);
        defaults.CopyTo(_live);
        _live.Save();
        LoadUi();
    }

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

    // -- Зависимые поля (спека Этап 6, §10) -------------------------------------------
    // Правила зависимости НЕ меняются и не добавляются: здесь только то, что уже следует
    // из настроек, но раньше было видно только по факту — выключенная погода оставляла
    // «Слева/Справа» и локацию живыми, выключенный таймер — пресеты, выключенный звук —
    // все ползунки. Пользователь кликал по тому, что заведомо ничего не меняет.

    private void UpdateDependencyStates()
    {
        var weather = WeatherEnabledBox.IsChecked == true;
        WeatherSideBox.IsEnabled = weather;
        WeatherLocationModeBox.IsEnabled = weather;
        ManualLocationPanel.IsEnabled = weather;

        var timer = TimerEnabledBox.IsChecked == true;
        TimerStopwatchBox.IsEnabled = timer;
        TimerDefaultSlider.IsEnabled = timer;
        TimerDefaultLabel.IsEnabled = timer;
        TimerPresetRow.IsEnabled = timer;

        var sound = SoundEnabledBox.IsChecked == true;
        SoundPackBox.IsEnabled = sound;
        VolumeSlider.IsEnabled = sound;
        VolumeLabel.IsEnabled = sound;
        VolumeEventsGrid.IsEnabled = sound;

        // Декоративные скорости по действиям не имеют смысла, когда всё движение уже
        // выключено мастером или сниженной анимацией. Значения сохраняются — галочка
        // только приглушает, чтобы было видно, что настройка не потеряна.
        var masterOff = SelectedTag(AnimSpeedBox) == nameof(AnimationSpeed.Off)
            || ReducedMotionBox.IsChecked == true;
        foreach (var box in PerActionSpeedBoxes())
        {
            box.IsEnabled = !masterOff;
            box.Opacity = masterOff ? 0.45 : 1.0;
        }
        AnimPulseEnabledBox.IsEnabled = !masterOff;
        AnimPulseEnabledBox.Opacity = masterOff ? 0.45 : 1.0;
    }

    private IEnumerable<ComboBox> PerActionSpeedBoxes() => new[]
    {
        AnimMorphInflateBox, AnimMorphCollapseBox, AnimUnreadPulseBox,
        AnimHoverBox, AnimClickPopBox, AnimFirstAppearWobbleBox,
    };

    // -- Превью анимаций (спека Этап 6, §9) ------------------------------------------
    // Один прогон по кнопке: выбранное появление, пауза, выбранный уход. Никакого
    // бесконечного цикла и никакой второй анимационной системы — только уже существующие
    // длительности (AnimationTiming/OverlayTokens) и кривые AnimEase, те самые, что крутят
    // островок. Сниженная анимация не «ускоряет» прогон, а убирает движение: элемент
    // появляется и исчезает на месте.

    private readonly DispatcherTimer _animPreviewTimer;
    private long _animPreviewStartMs;
    private int _animPreviewPhase;   // 0 — нет, 1 — появление, 2 — пауза, 3 — уход
    private ScaleTransform? _animPreviewScale;
    private TranslateTransform? _animPreviewMove;

    private void OnAnimPreviewClick(object? sender, RoutedEventArgs e)
    {
        _animPreviewTimer.Stop();
        _animPreviewStartMs = Environment.TickCount64;
        _animPreviewPhase = 1;
        _animPreviewTimer.Start();
    }

    private void OnAnimPreviewTick(object? sender, EventArgs e)
    {
        var speed = AnimationSpeedSelection();
        var reduced = ReducedMotionBox.IsChecked == true || !AnimationTiming.IsEnabled(speed);
        var appear = reduced ? 0 : OverlayTokens.MorphMs / 2;
        var dismiss = reduced ? 0 : OverlayTokens.MorphMs / 2;
        var hold = 320;
        var elapsed = Environment.TickCount64 - _animPreviewStartMs;

        if (elapsed <= appear)
        {
            var p = appear <= 0 ? 1.0 : elapsed / (double)appear;
            ApplyAnimPreviewFrame(SelectedTag(AppearStyleBox), p, leaving: false);
            return;
        }
        if (_animPreviewPhase == 1)
        {
            _animPreviewPhase = 2;
            _animPreviewStartMs = Environment.TickCount64;
            ApplyAnimPreviewFrame(SelectedTag(AppearStyleBox), 1.0, leaving: false);
            return;
        }
        var sinceHold = Environment.TickCount64 - _animPreviewStartMs;
        if (sinceHold <= hold)
        {
            ApplyAnimPreviewFrame(SelectedTag(DismissStyleBox), 0.0, leaving: true);
            return;
        }
        if (_animPreviewPhase == 2)
        {
            _animPreviewPhase = 3;
            _animPreviewStartMs = Environment.TickCount64;
            return;
        }
        var pd = dismiss <= 0 ? 1.0 : (sinceHold - hold) / (double)dismiss;
        if (pd >= 1.0)
        {
            _animPreviewTimer.Stop();
            _animPreviewPhase = 0;
            ResetAnimPreview();
            return;
        }
        ApplyAnimPreviewFrame(SelectedTag(DismissStyleBox), pd, leaving: true);
    }

    /// <summary>Кадр превью: тот же смысл, что у стилей островка, но на маленькой капсуле.</summary>
    private void ApplyAnimPreviewFrame(string? style, double p, bool leaving)
    {
        var eased = AnimEase.Ease("power2.out", p);
        var width = 150 + 90 * eased;

        var opacity = leaving ? 1.0 - p : eased;
        var scale = 1.0;
        var dy = 0.0;
        var dx = 0.0;

        switch (style)
        {
            case "Inflate":
                opacity = 1.0;
                break;
            case "SlideDown":
                dy = (leaving ? -1 : 1) * (1.0 - eased) * 14.0;
                break;
            case "FadeScale":
                scale = 0.94 + 0.06 * eased;
                break;
            case "Bounce":
                scale = AnimEase.Ease("spring.out", p);
                opacity = Math.Min(1.0, p * 2.0);
                break;
            case "Pop":
                scale = AnimEase.Ease("clickPop", p);
                opacity = Math.Min(1.0, p * 2.0);
                break;
            case "Collapse":
                opacity = 1.0;
                break;
            case "SlideUp":
                dy = -(1.0 - eased) * 14.0;
                break;
            case "FadeScaleOut":
                scale = 1.0 - 0.06 * eased;
                break;
            case "Ragged":
                dx = Math.Sin(p * Math.PI * 6) * (1.0 - p) * 3.5;
                break;
            case "Glitch":
                opacity = p < 0.34 ? 1.0 : p < 0.67 ? 0.55 : 0.15;
                break;
        }

        AnimPreviewCapsule.Opacity = Math.Clamp(opacity, 0, 1);
        AnimPreviewCapsule.RenderTransform = _animPreviewTransform(scale, dx, dy);
        if (leaving) AnimPreviewCapsule.Width = 150 + 90 * (1.0 - eased);
        else AnimPreviewCapsule.Width = 150 + 90 * eased;
    }

    private void ResetAnimPreview()
    {
        AnimPreviewCapsule.Opacity = 0;
        AnimPreviewCapsule.Width = 150;
        AnimPreviewCapsule.RenderTransform = null;
    }

    private void RefreshAnimPreviewHint()
    {
        var appear = SelectedTag(AppearStyleBox) ?? "—";
        var dismiss = SelectedTag(DismissStyleBox) ?? "—";
        var speed = SelectedTag(AnimSpeedBox) ?? "—";
        var reduced = ReducedMotionBox.IsChecked == true;
        AnimPreviewHint.Text = reduced
            ? "Сниженная анимация: проверка покажет мгновенное появление и уход без движения."
            : $"Появление: {appear} · Уход: {dismiss} · Скорость: {speed}";
    }

    private AnimationSpeed AnimationSpeedSelection() =>
        Enum.TryParse<AnimationSpeed>(SelectedTag(AnimSpeedBox), true, out var s)
            ? s : AnimationSpeed.Normal;

    private Transform? _animPreviewTransform(double scale, double dx, double dy)
    {
        var group = new TransformGroup();
        if (Math.Abs(scale - 1.0) > 0.0001)
        {
            _animPreviewScale ??= new ScaleTransform();
            _animPreviewScale.ScaleX = _animPreviewScale.ScaleY = scale;
            group.Children.Add(_animPreviewScale);
        }
        if (Math.Abs(dx) > 0.0001 || Math.Abs(dy) > 0.0001)
        {
            _animPreviewMove ??= new TranslateTransform();
            _animPreviewMove.X = dx;
            _animPreviewMove.Y = dy;
            group.Children.Add(_animPreviewMove);
        }
        return group.Children.Count == 0 ? null : group;
    }

    private static readonly (string Id, string Icon)[] NavEntries =
    [
        ("island", "layout-dashboard"),
        ("appearance", "palette"),
        // 1.20: было «mouse-pointer» — такого файла нет в вендоренном Lucide (в наборе 28
        // глифов), и пункт рисовался вообще без иконки, со сдвинутой подписью. «music» есть
        // и означает то же самое, что раздел: медиа, питание, таймер, буфер. Новый глиф не
        // придумываем — берём только из того же согласованного набора.
        ("behavior", "music"),
        ("monitor", "activity"),
        ("animation", "sparkles"),
        ("sound", "volume-2"),
        ("weather", "cloud-sun"),
        ("icons", "shapes"),
        ("other", "info"),
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

        // Показанная страница должна перемериться при показе. Скрытая панель не меряется,
        // и её ScrollViewer какое-то время несёт extent от прошлого раза; на самой длинной
        // странице «Анимации» прокрутка успевает упереться в этот протухший размер и не
        // доехать до кнопки «Проверить». Явная инвалидация после переключения закрывает
        // окно в один проход layout и стоит ничем.
        if (this.FindControl<ScrollViewer>($"Panel_{tag}") is { } shown)
            shown.InvalidateMeasure();
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
