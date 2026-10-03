using Xunit;

namespace NotifyIsland.Tests;

/// <summary>
/// Track G of docs/superpowers/plans/2026-09-30--animation-audit.md asked to stop the
/// <c>_tick</c> timer burning wakeups. The inventory that came out of that turned up a bigger
/// cost than the one on the list: <c>Paint()</c> runs on every 200 ms tick and calls
/// <c>IconPackService.Create</c> for the battery and weather icons, and <c>Create</c> did a
/// <c>File.ReadAllText</c> plus an <c>SvgSource.LoadFromSvg</c> on every call — 5 file reads and
/// 5 SVG parses per second, for icons that change a few times an hour.
///
/// The fix is a parsed-source cache. The cache itself lives in the app layer (it needs Avalonia's
/// <c>SvgSource</c>), but the KEY carries the one real decision in it, so that is in Core and
/// tested here: whether the tint hex has to be part of the key.
/// </summary>
public class IconCacheKeyTests
{
    [Fact]
    public void Monochrome_HexIsPartOfTheKey()
    {
        // The npm Meteocons monochrome pack ships black fills that get substituted into the XML
        // before parsing. Two tints are genuinely two different parsed sources.
        Assert.True(IconCacheKey.HexAffectsSource(MeteoconsMap.Monochrome));
        Assert.NotEqual(
            IconCacheKey.For(MeteoconsMap.Monochrome, "weather-clear", "#FFAAAAAA", 16),
            IconCacheKey.For(MeteoconsMap.Monochrome, "weather-clear", "#FFBBBBBB", 16));
    }

    [Theory]
    [InlineData(MeteoconsMap.Fill)]
    [InlineData(MeteoconsMap.Flat)]
    [InlineData(MeteoconsMap.Line)]
    public void NonMonochromeMeteocons_HexIsIgnored(string pack)
    {
        // These packs use currentColor, which Skia resolves as a paint-time tint. The parsed
        // source is identical for every theme, so keying on the hex would store the same SVG
        // again for each theme the user cycles through — pure cache growth for no correctness.
        Assert.False(IconCacheKey.HexAffectsSource(pack));
        Assert.Equal(
            IconCacheKey.For(pack, "weather-clear", "#FFAAAAAA", 16),
            IconCacheKey.For(pack, "weather-clear", "#FFBBBBBB", 16));
    }

    [Theory]
    [InlineData("Tabler")]
    [InlineData("Lucide")]
    public void OutlinePacks_HexIsPartOfTheKey(string pack)
    {
        // These DO substitute currentColor into the XML, so the tint is baked in at parse time.
        Assert.True(IconCacheKey.HexAffectsSource(pack));
        Assert.NotEqual(
            IconCacheKey.For(pack, "battery", "#FFAAAAAA", 16),
            IconCacheKey.For(pack, "battery", "#FFBBBBBB", 16));
    }

    [Fact]
    public void DifferentKey_IsADifferentEntry()
    {
        Assert.NotEqual(
            IconCacheKey.For("Tabler", "battery", "#FFFFFFFF", 16),
            IconCacheKey.For("Tabler", "bolt", "#FFFFFFFF", 16));
    }

    [Fact]
    public void DifferentSize_IsADifferentEntry()
    {
        // Width and Height are set on the Image, not baked into the source, so a strict reading
        // says the size need not be in the key. It is here anyway so the cache cannot be
        // misread as a source-only cache, and so a future change that bakes size into the SVG
        // has nowhere to silently break.
        Assert.NotEqual(
            IconCacheKey.For("Tabler", "battery", "#FFFFFFFF", 16),
            IconCacheKey.For("Tabler", "battery", "#FFFFFFFF", 24));
    }

    [Fact]
    public void Size_IsRoundedSoAFloatWobbleStillHits()
    {
        // Paint() computes icon size from the font size, which can come out as 15.99999999.
        // Without rounding that would miss the entry a previous call populated at 16 and
        // re-parse the file — exactly the waste this cache exists to remove.
        Assert.Equal(
            IconCacheKey.For("Tabler", "battery", "#FFFFFFFF", 16.0),
            IconCacheKey.For("Tabler", "battery", "#FFFFFFFF", 15.99999999));
    }

    [Fact]
    public void NullHex_DoesNotThrow_AndStillSeparatesOutlineFromMono()
    {
        var outline = IconCacheKey.For("Tabler", "battery", null, 16);
        var mono = IconCacheKey.For(MeteoconsMap.Monochrome, "weather-clear", null, 16);
        Assert.NotNull(outline);
        Assert.NotNull(mono);
        Assert.NotEqual(outline, mono);
    }
}
