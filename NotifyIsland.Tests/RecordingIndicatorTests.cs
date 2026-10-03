using NotifyIsland;
using Xunit;

namespace NotifyIsland.Tests;

public class RecordingIndicatorTests
{
    private static RecordingSample Mic(double level = 0.5) =>
        new() { MicrophoneActive = true, MicrophoneLevel = level };

    private static RecordingSample Idle() => new() { MicrophoneActive = false, MicrophoneLevel = 0 };

    private static RecordingSample Screen(string name = "OBS") =>
        new() { MicrophoneActive = false, MicrophoneLevel = 0, ScreenRecorderName = name };

    // -- Gating ------------------------------------------------------------------------------

    [Fact]
    public void One_active_poll_is_not_enough_to_announce()
    {
        // A single noisy sample is a door slam in another room, not a microphone in use.
        var gate = new RecordingGate();
        Assert.False(gate.Sample(Mic()).IsVisible);
    }

    [Fact]
    public void Two_consecutive_active_polls_announce()
    {
        var gate = new RecordingGate();
        gate.Sample(Mic());
        Assert.True(gate.Sample(Mic()).IsVisible);
    }

    [Fact]
    public void A_single_idle_poll_between_two_active_ones_resets_the_run()
    {
        // The run is CONSECUTIVE. A gap means the device went quiet and the count starts over.
        var gate = new RecordingGate();
        gate.Sample(Mic());
        gate.Sample(Idle());
        Assert.False(gate.Sample(Mic()).IsVisible);
        Assert.True(gate.Sample(Mic()).IsVisible);
    }

    [Fact]
    public void Going_away_takes_more_polls_than_arriving()
    {
        // The asymmetry is the point: a lamp that returns instantly after a pause stops being
        // believed.
        var gate = new RecordingGate();
        gate.Sample(Mic());
        gate.Sample(Mic());
        Assert.True(gate.Announced);

        for (var i = 0; i < RecordingGate.DefaultOffPolls - 1; i++)
            Assert.True(gate.Sample(Idle()).IsVisible);
        Assert.False(gate.Sample(Idle()).IsVisible);
        Assert.True(RecordingGate.DefaultOffPolls > RecordingGate.DefaultOnPolls);
    }

    [Fact]
    public void A_dip_below_the_floor_does_not_blink_the_indicator_off()
    {
        // Found by the gate's own tests failing: the badge reported "no activity" on the poll
        // where the microphone dipped between words, which is several times a second during
        // ordinary speech. The recording has not stopped — the LEVEL went to zero.
        var gate = new RecordingGate();
        gate.Sample(Mic(0.6));
        Assert.True(gate.Sample(Mic(0.6)).IsVisible);

        var dip = gate.Sample(new RecordingSample { MicrophoneActive = true, MicrophoneLevel = 0.0 });
        Assert.True(dip.IsVisible);
        Assert.Equal(RecordingActivity.Microphone, dip.Activity);
    }

    [Fact]
    public void The_level_falls_on_that_dip_while_the_indicator_stays()
    {
        var gate = new RecordingGate();
        for (var i = 0; i < 6; i++) gate.Sample(Mic(0.6));
        var before = gate.Level;
        var dip = gate.Sample(new RecordingSample { MicrophoneActive = true, MicrophoneLevel = 0.0 });
        Assert.True(dip.IsVisible);
        Assert.True(dip.Level < before, "the meter should fall while the lamp stays lit");
    }

    [Fact]
    public void Threshold_is_configurable_so_the_rule_can_be_tested_directly()
    {
        var gate = new RecordingGate { OnPolls = 1, OffPolls = 1 };
        Assert.True(gate.Sample(Mic()).IsVisible);
        Assert.False(gate.Sample(Idle()).IsVisible);
    }

    [Fact]
    public void A_level_below_the_floor_counts_as_idle_even_when_flagged_active()
    {
        // The source flags "this endpoint is a capture device"; the LEVEL is what says whether
        // anything is coming out of it. A flagged-but-silent device is not a microphone in use.
        var gate = new RecordingGate { OnPolls = 1, OffPolls = 1 };
        var quiet = new RecordingSample { MicrophoneActive = true, MicrophoneLevel = 0.001 };
        Assert.False(gate.Sample(quiet).IsVisible);
    }

    [Fact]
    public void Exactly_the_floor_is_still_a_level()
    {
        var gate = new RecordingGate { OnPolls = 1, OffPolls = 1 };
        var at = new RecordingSample { MicrophoneActive = true, MicrophoneLevel = RecordingGate.LevelFloor };
        Assert.True(gate.Sample(at).IsVisible);
    }

    // -- Screen ------------------------------------------------------------------------------

    [Fact]
    public void A_recorder_announces_on_the_first_poll_with_no_ramp()
    {
        // There is no level to debounce: a process being present is a fact, not a measurement.
        // Waiting two polls here would only add latency to something already certain.
        var gate = new RecordingGate();
        var state = gate.Sample(Screen());
        Assert.True(state.IsVisible);
        Assert.Equal(RecordingActivity.Screen, state.Activity);
    }

    [Fact]
    public void Mic_plus_recorder_is_both()
    {
        var gate = new RecordingGate { OnPolls = 1 };
        Assert.Equal(RecordingActivity.Microphone, gate.Sample(Mic()).Activity);
        Assert.Equal(RecordingActivity.Both, gate.Sample(new RecordingSample
        {
            MicrophoneActive = true,
            MicrophoneLevel = 0.4,
            ScreenRecorderName = "Xbox Game Bar",
        }).Activity);
    }

    [Fact]
    public void A_recorder_keeps_the_indicator_up_when_the_mic_goes_quiet()
    {
        var gate = new RecordingGate { OnPolls = 1, OffPolls = 2 };
        gate.Sample(Mic());
        for (var i = 0; i < 10; i++)
        {
            var state = gate.Sample(Screen());
            Assert.True(state.IsVisible);
        }
    }

    [Fact]
    public void A_recorder_that_exits_takes_the_indicator_down_it_raised()
    {
        var gate = new RecordingGate { OnPolls = 1, OffPolls = 1 };
        gate.Sample(Screen());
        Assert.True(gate.Sample(Idle()).IsVisible == false);
    }

    [Fact]
    public void A_blank_recorder_name_is_not_a_recorder()
    {
        var gate = new RecordingGate();
        var blank = new RecordingSample { ScreenRecorderName = "   " };
        Assert.False(gate.Sample(blank).IsVisible);
    }

    // -- Level -------------------------------------------------------------------------------

    [Fact]
    public void Level_rises_towards_the_reading_and_never_past_it()
    {
        var gate = new RecordingGate { OnPolls = 1, OffPolls = 1 };
        for (var i = 0; i < 20; i++) gate.Sample(Mic(0.8));
        Assert.InRange(gate.Level, 0.79, 0.81);
    }

    [Fact]
    public void Attack_is_faster_than_release()
    {
        // Rise fast so speech onset is not lost, fall slow so the meter does not strobe between
        // words.
        Assert.True(RecordingGate.Attack > RecordingGate.Release);
    }

    [Fact]
    public void Level_decays_to_zero_when_the_microphone_stops()
    {
        var gate = new RecordingGate { OnPolls = 1, OffPolls = 1 };
        for (var i = 0; i < 10; i++) gate.Sample(Mic(0.9));
        for (var i = 0; i < 40; i++) gate.Sample(Screen());   // recorder holds the badge open
        Assert.True(gate.Level < 0.01, $"level stayed at {gate.Level}");
    }

    [Fact]
    public void A_screen_only_indicator_never_shows_a_microphone_level()
    {
        // Otherwise a recorder with the mic closed keeps drawing the meter from before it closed.
        var gate = new RecordingGate { OnPolls = 1, OffPolls = 1 };
        gate.Sample(Mic(0.9));
        var state = gate.Sample(Screen());
        Assert.Equal(RecordingActivity.Screen, state.Activity);
        Assert.Equal(0, state.Level, 6);
    }

    [Fact]
    public void Level_never_leaves_the_unit_range()
    {
        var gate = new RecordingGate { OnPolls = 1, OffPolls = 1 };
        for (var i = 0; i < 30; i++) gate.Sample(Mic(i % 2 == 0 ? 0.0 : 1.0));
        for (var i = 0; i < 30; i++) gate.Sample(Mic(1.0));
        Assert.InRange(gate.Level, 0.0, 1.0);
    }

    [Fact]
    public void A_level_above_one_from_a_broken_source_does_not_explode_the_meter()
    {
        var gate = new RecordingGate { OnPolls = 1, OffPolls = 1 };
        for (var i = 0; i < 10; i++) gate.Sample(Mic(4.2));
        Assert.InRange(gate.Level, 0.0, 1.0);
    }

    // -- Labels ------------------------------------------------------------------------------

    [Theory]
    [InlineData(RecordingActivity.None, null, "")]
    [InlineData(RecordingActivity.Microphone, null, "Микрофон")]
    [InlineData(RecordingActivity.Screen, null, "Запись экрана")]
    [InlineData(RecordingActivity.Screen, "OBS", "Запись экрана (OBS)")]
    [InlineData(RecordingActivity.Both, null, "Микрофон · запись экрана")]
    [InlineData(RecordingActivity.Both, "OBS", "Микрофон · запись экрана (OBS)")]
    public void Label_says_what_is_happening(RecordingActivity activity, string? name, string expected)
    {
        Assert.Equal(expected, RecordingGate.LabelFor(activity, name));
    }

    [Fact]
    public void A_recorder_name_is_trimmed_in_the_label()
    {
        Assert.Equal("Запись экрана (OBS)", RecordingGate.LabelFor(RecordingActivity.Screen, "  OBS  "));
    }

    [Fact]
    public void The_microphone_alone_never_mentions_the_recorder()
    {
        // The name describes the SCREEN capture. Carrying it onto a mic-only reading would claim
        // something the source did not report.
        Assert.Equal("Микрофон", RecordingGate.LabelFor(RecordingActivity.Microphone, "OBS"));
    }
}
