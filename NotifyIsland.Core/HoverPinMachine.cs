using System;

namespace NotifyIsland;

/// <summary>
/// Pure hover-expand + click-pin state machine for Idle/Collapsed pill.
/// No UI / Win32 — drive from OverlayWindow with Tick(deltaMs).
/// </summary>
public enum HoverPinPhase
{
    /// <summary>Normal collapsed idle.</summary>
    Collapsed,
    /// <summary>Pointer entered; waiting <see cref="HoverDelayMs"/> before peek.</summary>
    HoverPending,
    /// <summary>Temporary hover peek (collapses on leave + grace).</summary>
    HoverExpanded,
    /// <summary>Pointer left while peeking; waiting grace before collapse.</summary>
    CollapsePending,
    /// <summary>Click-pinned expanded; survives pointer leave until click/Esc.</summary>
    Pinned
}

public sealed class HoverPinMachine
{
    private int _timerMs;

    public bool HoverExpandEnabled { get; set; } = true;
    public bool ClickPinEnabled { get; set; } = true;

    /// <summary>Delay after enter before peek (ms). Default 250.</summary>
    public int HoverDelayMs { get; set; } = OverlayTokens.HoverExpandDelayMs;

    /// <summary>Grace after leave before collapse (ms). Default 500.</summary>
    public int CollapseGraceMs { get; set; } = OverlayTokens.HoverCollapseGraceMs;

    public HoverPinPhase Phase { get; private set; } = HoverPinPhase.Collapsed;

    /// <summary>True when pill should show richer idle content (seconds + peek width).</summary>
    public bool IsContentExpanded =>
        Phase is HoverPinPhase.HoverExpanded or HoverPinPhase.CollapsePending or HoverPinPhase.Pinned;

    public bool IsPinned => Phase == HoverPinPhase.Pinned;

    /// <summary>True while hover-peek or pinned — softens/pauses idle breath.</summary>
    public bool SoftenBreath => IsContentExpanded;

    public void Configure(bool hoverEnabled, bool pinEnabled, int hoverDelayMs, int collapseGraceMs)
    {
        HoverExpandEnabled = hoverEnabled;
        ClickPinEnabled = pinEnabled;
        HoverDelayMs = Math.Clamp(hoverDelayMs, 0, 2000);
        CollapseGraceMs = Math.Clamp(collapseGraceMs, 0, 3000);
        if (!HoverExpandEnabled && Phase is HoverPinPhase.HoverPending or HoverPinPhase.HoverExpanded or HoverPinPhase.CollapsePending)
        {
            if (Phase != HoverPinPhase.Pinned)
                ResetToCollapsed();
        }
        if (!ClickPinEnabled && Phase == HoverPinPhase.Pinned)
            ResetToCollapsed();
    }

    public void PointerEnter()
    {
        if (!HoverExpandEnabled && Phase != HoverPinPhase.Pinned)
            return;

        switch (Phase)
        {
            case HoverPinPhase.Collapsed:
                if (!HoverExpandEnabled) return;
                Phase = HoverPinPhase.HoverPending;
                _timerMs = HoverDelayMs;
                break;
            case HoverPinPhase.CollapsePending:
                // Re-enter cancels grace → stay expanded
                Phase = HoverPinPhase.HoverExpanded;
                _timerMs = 0;
                break;
            case HoverPinPhase.HoverPending:
            case HoverPinPhase.HoverExpanded:
            case HoverPinPhase.Pinned:
                // already tracking / pinned
                break;
        }
    }

    public void PointerLeave()
    {
        switch (Phase)
        {
            case HoverPinPhase.HoverPending:
                ResetToCollapsed();
                break;
            case HoverPinPhase.HoverExpanded:
                Phase = HoverPinPhase.CollapsePending;
                _timerMs = CollapseGraceMs;
                break;
            case HoverPinPhase.CollapsePending:
            case HoverPinPhase.Pinned:
            case HoverPinPhase.Collapsed:
                break;
        }
    }

    /// <summary>
    /// Single click on Idle/Collapsed. Returns true if pin toggle was applied.
    /// When ClickPinEnabled is false, returns false (caller may open Action Center).
    /// </summary>
    public bool ClickTogglePin()
    {
        if (!ClickPinEnabled)
            return false;

        if (Phase == HoverPinPhase.Pinned)
        {
            // Unpin — if pointer still over pill, drop to HoverExpanded; else collapse.
            // Caller passes pointerOver via overload; default collapse then re-enter will expand.
            ResetToCollapsed();
            return true;
        }

        Phase = HoverPinPhase.Pinned;
        _timerMs = 0;
        return true;
    }

    /// <summary>Unpin (Esc) or force collapse peek.</summary>
    public void EscapeOrUnpin()
    {
        if (Phase == HoverPinPhase.Collapsed) return;
        ResetToCollapsed();
    }

    public void ResetToCollapsed()
    {
        Phase = HoverPinPhase.Collapsed;
        _timerMs = 0;
    }

    /// <summary>Advance pending timers. Returns true when Phase changed.</summary>
    public bool Tick(int deltaMs)
    {
        var dt = Math.Max(0, deltaMs);
        if (dt == 0) return false;
        var before = Phase;

        if (Phase == HoverPinPhase.HoverPending)
        {
            _timerMs -= dt;
            if (_timerMs <= 0)
            {
                Phase = HoverPinPhase.HoverExpanded;
                _timerMs = 0;
            }
        }
        else if (Phase == HoverPinPhase.CollapsePending)
        {
            _timerMs -= dt;
            if (_timerMs <= 0)
                ResetToCollapsed();
        }

        return Phase != before;
    }
}
