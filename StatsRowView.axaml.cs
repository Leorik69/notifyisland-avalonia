using Avalonia.Controls;
using Avalonia.Media;

namespace NotifyIsland;

/// <summary>
/// One reusable row of the System Stats surface: either a label+value pair or a single
/// centred caption line (the Date row). The panel instantiates as many of these as
/// <see cref="StatsLayout.ResolveRows"/> returns, in that order, and only writes text into
/// them on sampling ticks — the visual tree is rebuilt on settings changes, not per tick.
/// </summary>
public partial class StatsRowView : Avalonia.Controls.UserControl
{
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
            LabelBlock.IsVisible = !value;
            ValueBlock.IsVisible = !value;
        }
    }
}
