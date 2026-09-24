using System;

namespace NotifyIsland;

/// <summary>
/// Pure helpers for charge-pill / low-battery debounce (no WinRT).
/// </summary>
public static class BatteryAlertLogic
{
    public const int DefaultLowPercent = 20;
    public const int MinLowPercent = 5;
    public const int MaxLowPercent = 50;
    public const int LowResetHysteresis = 5;
    public const int ChargePercentStep = 5;
    public const int ChargePillMs = 3500;

    public static int ClampLowPercent(int value) =>
        Math.Clamp(value, MinLowPercent, MaxLowPercent);

    public static bool ShouldShowChargeConnect(bool? wasOnAc, bool nowOnAc, bool nowCharging) =>
        nowOnAc && nowCharging && wasOnAc != true;

    public static bool ShouldShowChargePercentBump(bool onAc, bool charging, int? prevPercent, int nowPercent)
    {
        if (!onAc || !charging || prevPercent is null) return false;
        return (prevPercent.Value / ChargePercentStep) != (nowPercent / ChargePercentStep);
    }

    public static bool ShouldShowLowAlert(bool onBattery, int percent, int threshold, bool alreadyFired) =>
        onBattery && !alreadyFired && percent <= threshold;

    public static bool ShouldResetLowFlag(bool onAc, int percent, int threshold) =>
        onAc || percent >= threshold + LowResetHysteresis;

    public static OverlayPayload ChargePayload(int percent) => new()
    {
        Title = "Зарядка",
        Subtitle = $"{Math.Clamp(percent, 0, 100)}%",
        Progress = Math.Clamp(percent, 0, 100) / 100.0,
        Playing = true
    };

    public static OverlayPayload LowBatteryPayload(int percent) => new()
    {
        Title = "Низкий заряд",
        Body = $"Осталось {Math.Clamp(percent, 0, 100)}%",
        Progress = Math.Clamp(percent, 0, 100) / 100.0,
        Playing = false
    };
}
