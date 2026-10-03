using System;

namespace NotifyIsland;

/// <summary>What the island is being asked to report about recording right now.</summary>
public enum RecordingActivity
{
    /// <summary>Nothing is capturing. The island says nothing.</summary>
    None = 0,
    /// <summary>A microphone is picking up signal.</summary>
    Microphone = 1,
    /// <summary>Something is capturing the screen (a heuristic, not an API — see the source).</summary>
    Screen = 2,
    /// <summary>Both at once. A screen recorder with its microphone open is the common case.</summary>
    Both = 3,
}

/// <summary>One poll's raw reading from the platform source.</summary>
public sealed class RecordingSample
{
    /// <summary>True when the capture device reports signal above the floor.</summary>
    public bool MicrophoneActive { get; init; }

    /// <summary>Peak level 0…1 as the audio API reports it. Meaningless when inactive.</summary>
    public double MicrophoneLevel { get; init; }

    /// <summary>Best-effort name of a screen recorder seen in the process list, or null.</summary>
    public string? ScreenRecorderName { get; init; }
}

/// <summary>What the island should draw, after the raw readings have been through the gate.</summary>
public sealed class RecordingIndicatorState
{
    public RecordingActivity Activity { get; init; } = RecordingActivity.None;
    /// <summary>Smoothed 0…1 level for the meter. 0 when there is no microphone activity.</summary>
    public double Level { get; init; }
    public string? ScreenRecorderName { get; init; }
    /// <summary>Short label for the tooltip: «Микрофон», «Запись экрана», or both.</summary>
    public string Label { get; init; } = "";
    public bool IsVisible => Activity != RecordingActivity.None;
}

/// <summary>
/// The decision half of the recording indicator: when a reading becomes an announcement, how long
/// a level has to stay high before the meter moves, and what the island says.
/// <para>
/// The hysteresis is the whole point. A capture device's peak level is not a step function — it
/// dips to zero between words, and a naive "is it above zero" test would make the indicator blink
/// several times a second during a normal conversation. Requiring <see cref="OnPolls"/> consecutive
/// active polls to switch ON, and <see cref="OffPolls"/> consecutive idle ones to switch back, is
/// what turns a noisy analogue signal into a steady lamp.
/// </para>
/// <para>
/// Pure and headless on purpose: the audio source is COM and untestable, and the rule about how
/// many samples make an announcement is exactly the part that is easy to get subtly wrong.
/// </para>
/// </summary>
public sealed class RecordingGate
{
    /// <summary>Consecutive active polls before the indicator appears. Configurable for tests.</summary>
    public int OnPolls { get; set; } = DefaultOnPolls;

    /// <summary>Consecutive idle polls before it goes away. Configurable for tests.</summary>
    public int OffPolls { get; set; } = DefaultOffPolls;

    /// <summary>Default for <see cref="OnPolls"/>.</summary>
    public const int DefaultOnPolls = 2;

    /// <summary>Default for <see cref="OffPolls"/>.</summary>
    public const int DefaultOffPolls = 4;

    /// <summary>Peak level below which the microphone counts as idle.</summary>
    public const double LevelFloor = 0.012;

    /// <summary>Attack smoothing: how much of a new reading the meter adopts per poll, 0…1.</summary>
    public const double Attack = 0.55;

    /// <summary>Release smoothing: faster decay than attack, so a finished word fades out.</summary>
    public const double Release = 0.30;

    private int _activeRun;
    private int _idleRun;
    private bool _announced;
    private double _level;
    private string? _recorderName;

    /// <summary>The level the meter currently shows, 0…1. Exposed for tests and for the tooltip.</summary>
    public double Level => _level;

    /// <summary>Whether the indicator is currently announced.</summary>
    public bool Announced => _announced;

    /// <summary>
    /// Feed one poll and get the state the island should draw.
    /// <para>
    /// The level is smoothed ONLY while the indicator is announced. Before the gate opens there is
    /// no meter on screen, so smoothing its input would just be a slower way of learning something
    /// we already know is false.
    /// </para>
    /// </summary>
    public RecordingIndicatorState Sample(RecordingSample sample)
    {
        // A broken or misreporting source must not be able to drive the meter off the end of its
        // scale: the raw value is clamped BEFORE smoothing, so every downstream number is already
        // inside 0…1 and no amount of polling can push it out.
        var raw = Math.Clamp(sample.MicrophoneLevel, 0, 1);
        var micActive = sample.MicrophoneActive && raw >= LevelFloor;
        var screen = !string.IsNullOrWhiteSpace(sample.ScreenRecorderName);

        if (micActive) { _activeRun++; _idleRun = 0; }
        else { _idleRun++; _activeRun = 0; }

        if (!_announced && (_activeRun >= OnPolls || screen)) _announced = true;
        else if (_announced && _idleRun >= OffPolls && !screen) _announced = false;

        if (!micActive && !screen) _level = 0;
        else if (micActive) _level = Smooth(_level, raw);
        else _level *= Release;                    // recorder holding the badge, mic went quiet

        if (screen) _recorderName = sample.ScreenRecorderName;
        else if (!micActive) _recorderName = null;

        if (!_announced)
            return new RecordingIndicatorState { Activity = RecordingActivity.None };

        // The meter is announced, so the level has to be on screen even on the poll where the
        // microphone happened to dip below the floor. Reporting None there is what made the badge
        // blink several times a second during a normal sentence — the exact failure the
        // hysteresis exists to prevent. What is still true is the RECORDING, not its level.
        var activity = (micActive, screen) switch
        {
            (true, true) => RecordingActivity.Both,
            (false, true) => RecordingActivity.Screen,
            _ => RecordingActivity.Microphone,
        };

        // Screen-only means the microphone is genuinely closed: a recorder running with its mic
        // shut must not keep drawing the meter from before it shut.
        var level = activity == RecordingActivity.Screen ? 0 : _level;

        return new RecordingIndicatorState
        {
            Activity = activity,
            Level = level,
            ScreenRecorderName = screen ? _recorderName : null,
            Label = LabelFor(activity, _recorderName),
        };
    }

    private static double Smooth(double current, double target)
    {
        var t = target > current ? Attack : Release;
        return current + (target - current) * t;
    }

    /// <summary>
    /// The tooltip text. Recording is the fact the user needs; the recorder's name is a detail
    /// they can act on, so it is included only when the screen is what is being captured.
    /// </summary>
    public static string LabelFor(RecordingActivity activity, string? recorderName)
    {
        var screen = activity is RecordingActivity.Screen or RecordingActivity.Both;
        var mic = activity is RecordingActivity.Microphone or RecordingActivity.Both;
        if (screen && mic)
            return string.IsNullOrWhiteSpace(recorderName)
                ? "Микрофон · запись экрана"
                : $"Микрофон · запись экрана ({recorderName.Trim()})";
        if (screen)
            return string.IsNullOrWhiteSpace(recorderName)
                ? "Запись экрана"
                : $"Запись экрана ({recorderName.Trim()})";
        if (mic) return "Микрофон";
        return "";
    }
}
