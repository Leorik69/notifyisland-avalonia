using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace NotifyIsland;

public enum IslandSoundKind
{
    Notify,
    Morph,
    Swipe,
    Error
}

/// <summary>
/// Quiet UI sounds via System.Media.SystemSounds (Windows) / MessageBeep fallback.
/// Respects SoundEnabled + SoundVolume (volume &lt; 0.05 = mute). No network, no hover/clock ticks.
/// </summary>
internal static class IslandSounds
{
    private static DateTime _lastPlayUtc = DateTime.MinValue;

    public static void Play(IslandSoundKind kind, AppSettings settings)
    {
        if (settings is null) return;
        if (!settings.SoundEnabled) return;
        if (settings.SoundVolume < 0.05) return;

        var now = DateTime.UtcNow;
        if ((now - _lastPlayUtc).TotalMilliseconds < 80) return;
        _lastPlayUtc = now;

        try
        {
            if (!TrySystemSounds(kind) && OperatingSystem.IsWindows())
                MessageBeep(MapBeep(kind));
        }
        catch (Exception ex)
        {
            AppLog.Warn("IslandSounds.Play failed", ex);
        }
    }

    private static bool TrySystemSounds(IslandSoundKind kind)
    {
        try
        {
            var ssType = Type.GetType("System.Media.SystemSounds, System")
                         ?? Type.GetType("System.Media.SystemSounds, System.Windows.Extensions")
                         ?? Type.GetType("System.Media.SystemSounds");
            if (ssType is null) return false;

            var prop = kind switch
            {
                IslandSoundKind.Notify => "Asterisk",
                IslandSoundKind.Error => "Hand",
                IslandSoundKind.Swipe => "Beep",
                _ => "Question"
            };
            var sound = ssType.GetProperty(prop, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (sound is null) return false;
            sound.GetType().GetMethod("Play", Type.EmptyTypes)?.Invoke(sound, null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static uint MapBeep(IslandSoundKind kind) => kind switch
    {
        IslandSoundKind.Notify => 0x00000040,
        IslandSoundKind.Error => 0x00000010,
        IslandSoundKind.Swipe => 0x00000000,
        _ => 0x00000020
    };

    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint uType);
}
