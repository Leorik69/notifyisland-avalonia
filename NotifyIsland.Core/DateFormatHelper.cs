using System;
using System.Globalization;

namespace NotifyIsland;

/// <summary>Collapsed-row date display variants (RU culture).</summary>
public enum DateFormat
{
    /// <summary>Hide date text.</summary>
    Off,
    /// <summary>«24 сен»</summary>
    DayMonth,
    /// <summary>«ср»</summary>
    WeekdayShort,
    /// <summary>«ср 24»</summary>
    WeekdayDay,
    /// <summary>«24.09»</summary>
    Numeric,
    /// <summary>«ср, 24 сен»</summary>
    FullShort
}

/// <summary>Formats <see cref="DateTime"/> for the collapsed island date chip.</summary>
public static class DateFormatHelper
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public static string Format(DateTime dt, DateFormat format) => format switch
    {
        DateFormat.Off => "",
        DateFormat.DayMonth => dt.ToString("d MMM", Ru).Replace(".", ""),
        DateFormat.WeekdayShort => dt.ToString("ddd", Ru).TrimEnd('.'),
        DateFormat.WeekdayDay => $"{dt.ToString("ddd", Ru).TrimEnd('.')} {dt.Day}",
        DateFormat.Numeric => dt.ToString("dd.MM", Ru),
        DateFormat.FullShort => $"{dt.ToString("ddd", Ru).TrimEnd('.')}, {dt.ToString("d MMM", Ru).Replace(".", "")}",
        _ => dt.ToString("d MMM", Ru).Replace(".", "")
    };

    /// <summary>Extra collapsed width (DIP) for the date chip beyond clock-only baseline.</summary>
    public static double ExtraCollapsedWidth(DateFormat format) => format switch
    {
        DateFormat.Off => 0,
        DateFormat.WeekdayShort => 28,
        DateFormat.DayMonth or DateFormat.WeekdayDay or DateFormat.Numeric => 48,
        DateFormat.FullShort => 78,
        _ => 48
    };
}
