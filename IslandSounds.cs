using System;
using System.IO;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace NotifyIsland;

internal enum IslandSound
{
    Notify,
    Error,
    Complete
}

internal static class IslandSounds
{
    private static MediaPlayer? _player;
    private static string? _dir;

    public static void Cue(IslandSound kind)
    {
        var prefs = PrefsStore.Current;
        if (!prefs.SoundsEnabled) return;
        var vol = Math.Clamp(prefs.SoundVolume, 0, 1);
        if (vol < 0.02) return;
        try
        {
            var path = Resolve(kind);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            _player ??= new MediaPlayer { AudioCategory = MediaPlayerAudioCategory.SoundEffects };
            _player.Volume = Math.Clamp(vol * 0.45, 0.02, 0.45);
            _player.Source = MediaSource.CreateFromUri(new Uri(path));
            _player.Play();
        }
        catch
        {
        }
    }

    public static void CueForKind(OverlayKind kind, string? template = null)
    {
        var t = NotifyTemplates.Normalize(template);
        var prefs = PrefsStore.Current;
        if (kind == OverlayKind.Error || t == "error")
        {
            if (prefs.SoundError) Cue(IslandSound.Error);
            return;
        }
        if (t == NotifyTemplates.Complete)
        {
            if (prefs.SoundComplete) Cue(IslandSound.Complete);
            return;
        }
        if (t == NotifyTemplates.Chat)
        {
            if (prefs.SoundChat) Cue(IslandSound.Notify);
            return;
        }
        if (kind is OverlayKind.Notification or OverlayKind.Stack || t is NotifyTemplates.Call or NotifyTemplates.Mail or NotifyTemplates.Warn or NotifyTemplates.System or NotifyTemplates.Calendar)
        {
            if (prefs.SoundNotify) Cue(IslandSound.Notify);
        }
    }

    private static string Resolve(IslandSound kind)
    {
        var media = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");
        var sys = kind switch
        {
            IslandSound.Error => Path.Combine(media, "Windows Error.wav"),
            IslandSound.Complete => File.Exists(Path.Combine(media, "Windows Notify System Generic.wav"))
                ? Path.Combine(media, "Windows Notify System Generic.wav")
                : Path.Combine(media, "Windows Notify Calendar.wav"),
            _ => Path.Combine(media, "Windows Notify.wav")
        };
        if (File.Exists(sys)) return sys;
        EnsureGenerated();
        return kind switch
        {
            IslandSound.Error => Path.Combine(_dir!, "error.wav"),
            IslandSound.Complete => Path.Combine(_dir!, "complete.wav"),
            _ => Path.Combine(_dir!, "notify.wav")
        };
    }

    private static void EnsureGenerated()
    {
        _dir ??= Path.Combine(Path.GetTempPath(), "NotifyIslandSounds");
        Directory.CreateDirectory(_dir);
        WriteTone(Path.Combine(_dir, "notify.wav"), 880, 0.11, 0.18);
        WriteTone(Path.Combine(_dir, "error.wav"), 220, 0.16, 0.16);
        WriteTone(Path.Combine(_dir, "complete.wav"), 660, 0.14, 0.16);
    }

    // Tiny PCM WAV (public-domain generated, not a copyrighted sample).
    private static void WriteTone(string path, double hz, double seconds, double amp)
    {
        const int rate = 22050;
        var n = (int)(rate * seconds);
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        var data = n * 2;
        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + data);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)1);
        bw.Write(rate);
        bw.Write(rate * 2);
        bw.Write((short)2);
        bw.Write((short)16);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(data);
        for (var i = 0; i < n; i++)
        {
            var t = i / (double)rate;
            var env = Math.Min(1, t / 0.012) * Math.Min(1, (seconds - t) / 0.03);
            var s = Math.Sin(2 * Math.PI * hz * t) * amp * env;
            bw.Write((short)Math.Clamp(s * 32767, short.MinValue, short.MaxValue));
        }
    }
}
