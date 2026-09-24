using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace NotifyIsland;

public enum IslandSoundKind
{
    Notify,
    Expand,
    Collapse,
    Swipe,
    Error,
    Hover
}

/// <summary>
/// Quiet UI sounds: WAV packs under Assets/Sounds/{pack}/ or SystemSounds fallback.
/// Respects SoundEnabled, SoundPack.Off, master + per-event volumes. Debounced; no clock ticks.
/// </summary>
internal static class IslandSounds
{
    private static DateTime _lastPlayUtc = DateTime.MinValue;
    private static DateTime _lastHoverUtc = DateTime.MinValue;
    private static readonly object Gate = new();
    /// <summary>Keep last SND_MEMORY buffer alive until next play (winmm async).</summary>
    private static byte[]? _winMmBuffer;
    private const int GlobalDebounceMs = 70;
    private const int HoverDebounceMs = 450;

    public static void Play(IslandSoundKind kind, AppSettings settings)
    {
        if (settings is null) return;
        if (!settings.SoundEnabled) return;
        if (settings.SoundPack == SoundPack.Off) return;

        var master = settings.SoundVolume;
        if (master < 0.05) return;

        var mult = kind switch
        {
            IslandSoundKind.Notify => settings.SoundVolNotify,
            IslandSoundKind.Expand => settings.SoundVolExpand,
            IslandSoundKind.Collapse => settings.SoundVolCollapse,
            IslandSoundKind.Swipe => settings.SoundVolSwipe,
            IslandSoundKind.Error => settings.SoundVolError,
            IslandSoundKind.Hover => settings.SoundVolHover,
            _ => 1.0
        };
        var volume = Math.Clamp(master * Math.Clamp(mult, 0.0, 1.0), 0.0, 1.0);
        if (volume < 0.05) return;

        var now = DateTime.UtcNow;
        lock (Gate)
        {
            if (kind == IslandSoundKind.Hover)
            {
                if ((now - _lastHoverUtc).TotalMilliseconds < HoverDebounceMs) return;
                _lastHoverUtc = now;
            }
            if ((now - _lastPlayUtc).TotalMilliseconds < GlobalDebounceMs) return;
            _lastPlayUtc = now;
        }

        try
        {
            if (settings.SoundPack == SoundPack.System)
            {
                if (TrySystemSounds(kind)) return;
                // Optional file fallback under Assets/Sounds/system/
                if (TryPlayWav("system", kind, volume)) return;
                if (OperatingSystem.IsWindows())
                    MessageBeep(MapBeep(kind));
                return;
            }

            var folder = settings.SoundPack == SoundPack.Ios ? "ios" : "nothing";
            if (!TryPlayWav(folder, kind, volume) && OperatingSystem.IsWindows())
            {
                // Last resort if pack files missing
                if (!TrySystemSounds(kind))
                    MessageBeep(MapBeep(kind));
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("IslandSounds.Play failed", ex);
        }
    }

    private static string FileName(IslandSoundKind kind) => kind switch
    {
        IslandSoundKind.Notify => "notify.wav",
        IslandSoundKind.Expand => "expand.wav",
        IslandSoundKind.Collapse => "collapse.wav",
        IslandSoundKind.Swipe => "swipe.wav",
        IslandSoundKind.Error => "error.wav",
        IslandSoundKind.Hover => "hover.wav",
        _ => "notify.wav"
    };

    private static bool TryPlayWav(string packFolder, IslandSoundKind kind, double volume)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", packFolder, FileName(kind));
            if (!File.Exists(path))
            {
                // Dev / alternate layout
                path = Path.Combine(AppContext.BaseDirectory, "sounds", packFolder, FileName(kind));
                if (!File.Exists(path)) return false;
            }

            var data = ScaleWavVolume(File.ReadAllBytes(path), (float)volume);
            return PlaySoundPlayer(data) || PlayWinMmFromMemory(data);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Scale PCM 16-bit mono/stereo WAV amplitude (fmt chunk volume).</summary>
    internal static byte[] ScaleWavVolume(byte[] wav, float volume)
    {
        volume = Math.Clamp(volume, 0f, 1f);
        if (volume >= 0.999f) return wav;
        if (wav.Length < 44) return wav;

        var copy = (byte[])wav.Clone();
        // Find 'data' chunk
        var dataOffset = -1;
        for (var i = 12; i + 8 < copy.Length; i++)
        {
            if (copy[i] == (byte)'d' && copy[i + 1] == (byte)'a' &&
                copy[i + 2] == (byte)'t' && copy[i + 3] == (byte)'a')
            {
                dataOffset = i + 8;
                break;
            }
        }
        if (dataOffset < 0 || dataOffset >= copy.Length) return copy;

        for (var i = dataOffset; i + 1 < copy.Length; i += 2)
        {
            var sample = (short)(copy[i] | (copy[i + 1] << 8));
            var scaled = (int)Math.Round(sample * volume);
            scaled = Math.Clamp(scaled, short.MinValue, short.MaxValue);
            copy[i] = (byte)(scaled & 0xFF);
            copy[i + 1] = (byte)((scaled >> 8) & 0xFF);
        }
        return copy;
    }

    private static bool PlaySoundPlayer(byte[] wavBytes)
    {
        try
        {
            var spType = Type.GetType("System.Media.SoundPlayer, System")
                         ?? Type.GetType("System.Media.SoundPlayer, System.Windows.Extensions")
                         ?? Type.GetType("System.Media.SoundPlayer");
            if (spType is null) return false;

            var ms = new MemoryStream(wavBytes, writable: false);
            var player = Activator.CreateInstance(spType, ms);
            if (player is null) return false;
            spType.GetMethod("Play", Type.EmptyTypes)?.Invoke(player, null);
            // Keep stream alive briefly; SoundPlayer reads async on Windows
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    Thread.Sleep(500);
                    (player as IDisposable)?.Dispose();
                    ms.Dispose();
                }
                catch { /* ignore */ }
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool PlayWinMmFromMemory(byte[] wavBytes)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            lock (Gate) { _winMmBuffer = wavBytes; }
            // SND_MEMORY | SND_ASYNC | SND_NODEFAULT
            const uint flags = 0x0004 | 0x0001 | 0x0002;
            return PlaySound(wavBytes, IntPtr.Zero, flags);
        }
        catch
        {
            return false;
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
                IslandSoundKind.Hover => "Beep",
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
        IslandSoundKind.Hover => 0x00000000,
        _ => 0x00000020
    };

    [DllImport("winmm.dll", EntryPoint = "PlaySound", CharSet = CharSet.Auto)]
    private static extern bool PlaySound(byte[] pszSound, IntPtr hmod, uint fdwSound);

    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint uType);
}
