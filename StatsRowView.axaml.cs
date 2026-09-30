using Avalonia.Controls;
using Avalonia.Media;

namespace NotifyIsland;

/// <summary>
/// One reusable row of the System Stats surface: a label+value pair, a single centred caption
/// line (the Date row), or — since 1.13 — a status row with a bar and a button cluster (Media
/// and Timer). The panel instantiates as many of these as
/// <see cref="StatsLayout.ResolveRows"/> returns, in that order, and only writes text into
/// them on ticks — the visual tree is rebuilt on settings changes, not per tick.
/// </summary>
public partial class StatsRowView : Avalonia.Controls.UserControl
{
    /// <summary>Fired when the row's first/second/third action button is pressed.</summary>
    public event Action<int>? ActionClicked;

    public StatsRowView()
    {
        InitializeComponent();
    }

    /// <summary>Caption text of a caption row (the Date row). Ignored on pair rows.</summary>
    public string Caption
    {
        get => CaptionBlock.Text ?? "";
        set => CaptionBlock.Text = value;
    }

    /// <summary>Left-hand label of a pair row. Ignored on caption rows.</summary>
    public string Label
    {
        get => LabelBlock.Text ?? "";
        set => LabelBlock.Text = value;
    }

    /// <summary>Right-hand value of a pair row. Ignored on caption rows.</summary>
    public string Value
    {
        get => ValueBlock.Text ?? "";
        set => ValueBlock.Text = value;
    }

    /// <summary>Threshold colour of a pair row's value; always normal on caption rows.</summary>
    public IBrush? ValueBrush
    {
        get => ValueBlock.Foreground;
        set => ValueBlock.Foreground = value;
    }

    /// <summary>True → the single centred caption line; false → the label+value pair.</summary>
    public bool IsCaptionRow
    {
        get => CaptionBlock.IsVisible;
        set
        {
            CaptionBlock.IsVisible = value;
            // 1.16: the caption row owns a 1 DIP rule above it (see RowDivider in the XAML).
            // The rule follows the date row, so a custom row order that puts the date first
            // puts the rule first too — the separator separates the date from the metrics,
            // it is not a fixed header under the first row.
            RowDivider.IsVisible = value;
            // 1.16: a caption row must not ALSO show the pair furniture. SyncStatsRows assigns
            // Value = "—" to every row before it knows the row's shape, so without this the
            // date row rendered a centred caption and a stray "—" pinned to the right edge —
            // the exact "values must not touch the edge" defect the monitor layout is about.
            LabelBlock.IsVisible = !value;
            ValueBlock.IsVisible = !value;
            StatusDetail.IsVisible = !value && !string.IsNullOrWhiteSpace(StatusDetail.Text);
        }
    }

    /// <summary>Accent of the status row's 2 DIP bar. Ignored when the bar is hidden.</summary>
    public IBrush? StatusBarBrush
    {
        get => StatusBar.Foreground;
        set => StatusBar.Foreground = value;
    }

    /// <summary>
    /// Fill a status row (Media / Timer) from a plain model, and switch the row into its
    /// status shape. Every other kind of row leaves this untouched, so a metric row can
    /// never accidentally grow a bar or a button.
    /// <para>
    /// The action cluster is three fixed buttons rather than a variable list: the two rows
    /// that use it have a fixed set (⏮ ▶ ⏭ and ⏸ +1 ✕), and a dynamic button list would mean
    /// re-measuring the row every time the source changed.
    /// </para>
    /// </summary>
    public void SetStatus(StatusRowModel model)
    {
        LabelBlock.Text = model.Label ?? "";
        ValueBlock.Text = model.Value ?? "";
        StatusDetail.Text = model.Detail ?? "";
        StatusDetail.IsVisible = !string.IsNullOrWhiteSpace(model.Detail);

        // A null Progress means "this source has no meaningful fraction" (a stopwatch, an
        // unknown track length). The bar is then hidden rather than drawn empty: an empty
        // 2 DIP line under a row reads as a layout bug, not as a lack of information.
        var hasBar = model.Progress is { } frac;
        StatusBar.IsVisible = hasBar;
        if (model.Progress is { } f)
            StatusBar.Value = Math.Clamp(f, 0, 1);

        var buttons = model.Kind switch
        {
            StatusRowKind.Media => new[] { "⏮", model.Playing ? "⏸" : "▶", "⏭" },
            StatusRowKind.Timer => new[] { model.Playing ? "⏸" : "▶", "+1", "✕" },
            _ => Array.Empty<string>()
        };
        var showActions = model.Active && buttons.Length > 0;
        ActionsHost.IsVisible = showActions;
        // The value yields its column to the buttons only while they are shown, so a
        // parked/empty status row is not left with a hole where the controls were.
        ValueBlock.Margin = showActions ? new Avalonia.Thickness(0, 0, 0, 0) : default;
        if (!showActions)
            StatusDetail.Margin = new Avalonia.Thickness(0, 0, 0, 0);

        SetButton(ActionA, buttons.Length > 0 ? buttons[0] : "");
        SetButton(ActionB, buttons.Length > 1 ? buttons[1] : "");
        SetButton(ActionC, buttons.Length > 2 ? buttons[2] : "");
        // Only the timer's cancel glyph is destructive; the media row has no such action.
        ActionC.Classes.Set("danger", model.Kind == StatusRowKind.Timer);

        var tooltips = model.Kind switch
        {
            StatusRowKind.Media => new[] { "Предыдущий трек", "Пауза / воспроизведение", "Следующий трек" },
            StatusRowKind.Timer => new[] { "Пауза / продолжить", "Добавить минуту", "Отменить таймер" },
            _ => Array.Empty<string>()
        };
        SetTip(ActionA, tooltips.ElementAtOrDefault(0) ?? "");
        SetTip(ActionB, tooltips.ElementAtOrDefault(1) ?? "");
        SetTip(ActionC, tooltips.ElementAtOrDefault(2) ?? "");
    }

    // Avalonia.Controls.Button spelled out: the project globally imports WinForms, so the
    // bare name is ambiguous between the two Button types.
    private static void SetButton(Avalonia.Controls.Button b, string glyph)
    {
        b.Content = new TextBlock
        {
            Text = glyph,
            FontSize = 11,
            // The cancel glyph is red; the glyph text inherits the button's Foreground
            // otherwise, so the danger style would not show.
            Foreground = (Avalonia.Media.IBrush?)b.Foreground,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        b.IsVisible = glyph.Length > 0;
    }

    private static void SetTip(Avalonia.Controls.Button b, string tip) =>
        ToolTip.SetTip(b, string.IsNullOrWhiteSpace(tip) ? null : tip);

    private void OnActionAClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ActionClicked?.Invoke(0);
    private void OnActionBClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ActionClicked?.Invoke(1);
    private void OnActionCClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ActionClicked?.Invoke(2);
}
