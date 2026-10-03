using System.IO;
using System.Text.Json;
using Xunit;

namespace NotifyIsland.Tests;

public class SettingsTransferTests
{
    private static AppSettings NonDefault() => new()
    {
        ThemePreset = ThemePreset.Ocean,
        Opacity = 0.6,
        ColorAccent = "#00C2A8",
        ReducedMotion = true,
        WeatherEnabled = false,
    };

    [Fact]
    public void Export_round_trips_through_import()
    {
        var src = NonDefault();
        var back = AppSettings.TryImport(src.ToJson(), out var error);
        Assert.NotNull(back);
        Assert.Equal("", error);
        Assert.Equal(ThemePreset.Ocean, back!.ThemePreset);
        Assert.Equal(0.6, back.Opacity, 3);
        Assert.Equal("#00C2A8", back.ColorAccent);
        Assert.True(back.ReducedMotion);
        Assert.False(back.WeatherEnabled);
    }

    [Fact]
    public void Old_pascal_case_export_with_numeric_enums_still_imports()
    {
        // The format the previous export wrote: System.Text.Json defaults.
        var legacy = JsonSerializer.Serialize(NonDefault());
        Assert.Contains("\"ThemePreset\"", legacy);
        var back = AppSettings.TryImport(legacy, out _);
        Assert.NotNull(back);
        Assert.Equal(ThemePreset.Ocean, back!.ThemePreset);
        Assert.True(back.ReducedMotion);
        Assert.False(back.WeatherEnabled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    [InlineData("[1,2,3]")]
    public void Garbage_is_rejected_with_a_reason(string json)
    {
        var back = AppSettings.TryImport(json, out var error);
        Assert.Null(back);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Atomic_write_replaces_existing_file_and_leaves_no_temp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ni-atomic-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "settings.json");
            AppSettings.WriteAtomically(path, "first");
            AppSettings.WriteAtomically(path, "second");
            Assert.Equal("second", File.ReadAllText(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { Directory.Delete(dir, true); }
    }
}