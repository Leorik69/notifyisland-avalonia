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

        // 2026-10-02 perf pass: each action button's TextBlock is built once, here. SetStatus
        // used to construct a fresh TextBlock for all three buttons on every call, and
        // SetStatus runs on the 200 ms tick — 15 throwaway controls a second, each of which
        // forced the row to re-measure, to display a glyph that usually had not changed.
        foreach (var b in new[] { ActionA, ActionB, ActionC })
            b.Content = MakeGlyph(b);
    }

    // The cancel glyph is red; the glyph text inherits the button's Foreground otherwise, so the
    // danger style would not show.
    private static Avalonia.Controls.TextBlock MakeGlyph(Avalonia.Controls.Button b) => new()
    {
        FontSize = 11,
        Foreground = (Avalonia.Media.IBrush?)b.Foreground,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
    };

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
    /// Whether this row may draw its own progress bar (1.17).
    /// <para>
    /// The capsule already arbitrates its single bottom strip between clipboard, media and
    /// timer (<c>CapsuleProgressBand</c>), and the source that WINS that strip is drawn
    /// there. Drawing its bar here as well would put two indicators on one activity. So
    /// the window sets this false for the band's owner and leaves it true for the losers —
    /// which is exactly what the band arbiter promises: "a loser does not disappear, it
    /// stays in the System Monitor row that has its own label and value".
    /// </para>
    /// <para>
    /// A null fraction never shows a bar at all, whatever this says: an empty 2 DIP line
    /// under a row reads as a layout bug, not as a lack of information.
    /// </para>
    /// </summary>
    public bool ShowRowProgress { get; set; } = true;

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
        // ShowRowProgress additionally hides it for the source that owns the capsule band,
        // so one activity never draws two bars.
        var hasBar = model.Progress is { } frac && ShowRowProgress;
        StatusBar.IsVisible = hasBar;
        if (model.Progress is { } f)
            StatusBar.Value = Math.Clamp(f, 0, 1);

        // The two clusters are fixed (see the class note above), so their glyphs and tooltips are
        // constants hoisted to statics rather than re-allocated per call and read back out
        // through a LINQ ElementAtOrDefault that allocated an enumerator to reach index 0.
        var buttons = model.Kind switch
        {
            StatusRowKind.Media => MediaGlyphs(model.Playing),
            StatusRowKind.Timer => TimerGlyphs(model.Playing),
            _ => Array.Empty<string>()
        };
        var showActions = model.Active && buttons.Length > 0;
        ActionsHost.IsVisible = showActions;

        SetButton(ActionA, buttons.Length > 0 ? buttons[0] : "");
        SetButton(ActionB, buttons.Length > 1 ? buttons[1] : "");
        SetButton(ActionC, buttons.Length > 2 ? buttons[2] : "");
        // Only the timer's cancel glyph is destructive; the media row has no such action.
        ActionC.Classes.Set("danger", model.Kind == StatusRowKind.Timer);

        var tooltips = model.Kind switch
        {
            StatusRowKind.Media => MediaTips,
            StatusRowKind.Timer => TimerTips,
            _ => Array.Empty<string>()
        };
        SetTip(ActionA, tooltips.Length > 0 ? tooltips[0] : null);
        SetTip(ActionB, tooltips.Length > 1 ? tooltips[1] : null);
        SetTip(ActionC, tooltips.Length > 2 ? tooltips[2] : null);
    }

    private static readonly string[] MediaTips = { "Предыдущий трек", "Пауза / воспроизведение", "Следующий трек" };
    private static readonly string[] TimerTips = { "Пауза / продолжить", "Добавить минуту", "Отменить таймер" };

    private static string[] MediaGlyphs(bool playing) =>
        playing ? new[] { "⏮", "⏸", "⏭" } : new[] { "⏮", "▶", "⏭" };

    private static string[] TimerGlyphs(bool playing) =>
        playing ? new[] { "⏸", "+1", "✕" } : new[] { "▶", "+1", "✕" };

    // Avalonia.Controls.Button spelled out: the project globally imports WinForms, so the
    // bare name is ambiguous between the two Button types.
    private static void SetButton(Avalonia.Controls.Button b, string glyph)
    {
        // The TextBlock was built in the constructor; only its text is written now.
        if (b.Content is Avalonia.Controls.TextBlock tb)
            tb.Text = glyph;
        b.IsVisible = glyph.Length > 0;
    }

    private static void SetTip(Avalonia.Controls.Button b, string? tip)
    {
        // Only re-arm Avalonia's tooltip timer when the tip actually changed — the tooltips are
        // constant for the life of the row, so this used to fire fifteen times a second.
        var value = string.IsNullOrWhiteSpace(tip) ? null : tip;
        if (!string.Equals(b.GetValue(ToolTip.TipProperty) as string, value, StringComparison.Ordinal))
            ToolTip.SetTip(b, value);
    }

    private void OnActionAClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ActionClicked?.Invoke(0);
    private void OnActionBClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ActionClicked?.Invoke(1);
    private void OnActionCClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ActionClicked?.Invoke(2);
}
