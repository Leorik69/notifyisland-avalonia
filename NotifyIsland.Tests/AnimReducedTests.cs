using System.Text.Json;
using Xunit;

namespace NotifyIsland.Tests;

public class AnimReducedTests
{
    [Fact]
    public void OsAnimationsOff_IsReduced()
    {
        // "Показывать анимации в Windows" off — SystemParameters.ClientAreaAnimation == false.
        Assert.True(AnimReduced.Resolve(osAnimationsEnabled: false, userReducedMotion: false));
    }

    [Fact]
    public void OsAnimationsOff_StaysReduced_EvenIfTheUserDidNotAsk()
    {
        // Otherwise our default false would make the OS accessibility setting a no-op.
        Assert.True(AnimReduced.Resolve(false, false));
    }

    [Fact]
    public void UserToggleOn_IsReduced_EvenWhenTheOsAnimates()
    {
        Assert.True(AnimReduced.Resolve(osAnimationsEnabled: true, userReducedMotion: true));
    }

    [Fact]
    public void OsAnimates_AndNoUserRequest_IsNotReduced()
    {
        Assert.False(AnimReduced.Resolve(osAnimationsEnabled: true, userReducedMotion: false));
    }

    [Fact]
    public void DefaultSettings_KeepAnimations()
    {
        // Safe default for every existing install: nothing changes until someone opts in.
        Assert.False(new AppSettings().ReducedMotion);
        Assert.False(AnimReduced.Resolve(true, new AppSettings().ReducedMotion));
    }

    [Fact]
    public void UserToggle_SurvivesASettingsCopy()
    {
        var src = new AppSettings { ReducedMotion = true };
        var dst = new AppSettings();
        src.CopyTo(dst);
        Assert.True(dst.ReducedMotion);
    }

    [Fact]
    public void OldSettingsJson_WithoutTheKey_DeserializesToTheDefault()
    {
        // settings.json written by 1.12.3 has no ReducedMotion member; a plain bool keeps the
        // initialiser value, so no migration step and no type change is needed.
        const string legacy = "{\"AnimationSpeed\":\"Slow\",\"AnimPulseEnabled\":true}";
        var s = JsonSerializer.Deserialize<AppSettings>(legacy);
        Assert.NotNull(s);
        Assert.False(s!.ReducedMotion);
    }

    [Fact]
    public void SettingsJson_RoundTripsTheFlag()
    {
        var json = JsonSerializer.Serialize(new AppSettings { ReducedMotion = true });
        Assert.Contains("ReducedMotion", json);
        var back = JsonSerializer.Deserialize<AppSettings>(json);
        Assert.NotNull(back);
        Assert.True(back!.ReducedMotion);
    }
}
