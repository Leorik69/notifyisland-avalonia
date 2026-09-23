using System;
using Xunit;

namespace NotifyIsland.Tests;

public class DateFormatHelperTests
{
    private static readonly DateTime Sample = new(2026, 9, 24, 14, 5, 0); // Thursday

    [Fact]
    public void Off_ReturnsEmpty()
    {
        Assert.Equal("", DateFormatHelper.Format(Sample, DateFormat.Off));
        Assert.Equal(0, DateFormatHelper.ExtraCollapsedWidth(DateFormat.Off));
    }

    [Fact]
    public void DayMonth_HasDayAndMonth()
    {
        var s = DateFormatHelper.Format(Sample, DateFormat.DayMonth);
        Assert.Contains("24", s);
        Assert.False(string.IsNullOrWhiteSpace(s));
        Assert.DoesNotContain(".", s); // trimmed ru "сент."
    }

    [Fact]
    public void WeekdayShort_NonEmpty()
    {
        var s = DateFormatHelper.Format(Sample, DateFormat.WeekdayShort);
        Assert.False(string.IsNullOrWhiteSpace(s));
        Assert.True(s.Length <= 4);
    }

    [Fact]
    public void WeekdayDay_ContainsDay()
    {
        var s = DateFormatHelper.Format(Sample, DateFormat.WeekdayDay);
        Assert.Contains("24", s);
    }

    [Fact]
    public void Numeric_DdMm()
    {
        var s = DateFormatHelper.Format(Sample, DateFormat.Numeric);
        Assert.Equal("24.09", s);
    }

    [Fact]
    public void FullShort_HasCommaStyleParts()
    {
        var s = DateFormatHelper.Format(Sample, DateFormat.FullShort);
        Assert.Contains("24", s);
        Assert.Contains(",", s);
    }

    [Fact]
    public void ExtraWidth_FullShortWidest()
    {
        Assert.True(DateFormatHelper.ExtraCollapsedWidth(DateFormat.FullShort)
            > DateFormatHelper.ExtraCollapsedWidth(DateFormat.DayMonth));
        Assert.True(DateFormatHelper.ExtraCollapsedWidth(DateFormat.DayMonth)
            > DateFormatHelper.ExtraCollapsedWidth(DateFormat.WeekdayShort));
    }
}
