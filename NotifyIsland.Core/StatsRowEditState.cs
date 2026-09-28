namespace NotifyIsland;

/// <summary>One line of the custom-row editor: a row plus whether the user keeps it visible.</summary>
public readonly record struct StatsRowEditItem(StatsRow Row, bool IsVisible);

/// <summary>
/// Pure model behind the Settings → Монитор → «Свой набор» row editor. The editor always shows
/// all <see cref="AllRows"/> rows (each with a visibility checkbox) in the user's chosen order;
/// unchecking a row hides it but keeps its slot, so a row can be parked and moved back.
/// Only the visible rows, in order, are written to <see cref="AppSettings.StatsRows"/>.
///
/// The editor is the one place where rows get checked, unchecked and reordered, so all three
/// operations live here as pure functions returning a new state — the Avalonia layer just
/// re-renders whatever comes back. No platform dependencies, so it is directly unit-testable.
/// </summary>
public sealed class StatsRowEditState
{
    /// <summary>Every row, in <see cref="StatsRow"/> declaration order (CPU → дата).</summary>
    public static IReadOnlyList<StatsRow> AllRows { get; } = Enum.GetValues<StatsRow>();

    private readonly List<StatsRowEditItem> _items;

    /// <summary>Default editor state: the Full preset, everything visible.</summary>
    public StatsRowEditState() : this(StatsLayout.FullRows) { }

    /// <summary>
    /// Seed from a row list (typically the draft's <see cref="AppSettings.StatsRows"/>, which
    /// Normalize() has already reconciled with the active preset). Rows in the list become the
    /// visible head of the editor in their given order; every row the list does not mention is
    /// appended below, unchecked — so switching Brief → Свой набор offers «Память / Сеть / Дата»
    /// as parked rows instead of losing them.
    /// </summary>
    public StatsRowEditState(IReadOnlyList<StatsRow>? rows)
    {
        var items = new List<StatsRowEditItem>(AllRows.Count);
        var seen = new HashSet<StatsRow>();
        if (rows is not null)
        {
            foreach (var row in rows)
                if (Enum.IsDefined(typeof(StatsRow), row) && seen.Add(row))
                    items.Add(new StatsRowEditItem(row, true));
        }
        foreach (var row in AllRows)
            if (seen.Add(row))
                items.Add(new StatsRowEditItem(row, false));
        _items = items;
    }

    /// <summary>The editor rows, in display order (visible and parked alike).</summary>
    public IReadOnlyList<StatsRowEditItem> Items => _items;

    /// <summary>Is this row currently kept in the surface?</summary>
    public bool IsVisible(StatsRow row)
    {
        foreach (var i in _items) if (i.Row == row) return i.IsVisible;
        return false;
    }

    /// <summary>Check or uncheck a row, keeping its position in the order.</summary>
    public StatsRowEditState WithVisibility(StatsRow row, bool visible)
    {
        var next = new List<StatsRowEditItem>(_items.Count);
        var found = false;
        foreach (var i in _items)
        {
            if (i.Row == row) { next.Add(new StatsRowEditItem(row, visible)); found = true; }
            else next.Add(i);
        }
        // A row the state somehow does not know lands at the end rather than vanishing.
        if (!found) next.Add(new StatsRowEditItem(row, visible));
        return new StatsRowEditState(next);
    }

    /// <summary>
    /// Move a row one slot up (negative <paramref name="delta"/>) or down. A move that would run
    /// off either end is a no-op returning an equal state, so the buttons never need to disable.
    /// </summary>
    public StatsRowEditState Move(StatsRow row, int delta)
    {
        var from = -1;
        for (var i = 0; i < _items.Count; i++) if (_items[i].Row == row) { from = i; break; }
        if (from < 0 || delta == 0) return this;
        var to = Math.Clamp(from + delta, 0, _items.Count - 1);
        if (to == from) return this;
        var next = new List<StatsRowEditItem>(_items);
        next.RemoveAt(from);
        next.Insert(to, _items[from]);
        return new StatsRowEditState(next);
    }

    /// <summary>Rows to persist in <see cref="AppSettings.StatsRows"/>: visible ones, in order.</summary>
    public IReadOnlyList<StatsRow> ToCustomRows()
    {
        var kept = new List<StatsRow>(_items.Count);
        foreach (var i in _items) if (i.IsVisible) kept.Add(i.Row);
        return kept;
    }

    /// <summary>
    /// What the surface would actually show for a given preset. For Свой набор this is
    /// <see cref="ToCustomRows"/> with the same empty-check fallback Normalize() applies, so the
    /// settings preview and the live pill can never disagree.
    /// </summary>
    public IReadOnlyList<StatsRow> ResolveRows(StatsPreset preset) =>
        StatsLayout.ResolveRows(preset, ToCustomRows());

    /// <summary>Height the pill would take for a given preset, in DIP.</summary>
    public double HeightFor(StatsPreset preset) =>
        StatsLayout.StatsHeightFor(ResolveRows(preset).Count);

    private StatsRowEditState(List<StatsRowEditItem> items) => _items = items;
}
