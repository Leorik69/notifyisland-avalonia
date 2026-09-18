using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using NotifyIsland.Core;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Styling;
using Avalonia.Threading;

namespace NotifyIsland;

internal static class IslandAnimator
{
    public static void WireChrome(Control target)
    {
        var d = TimeSpan.FromMilliseconds(Motion.FastMs);
        target.Transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(Motion.FadeMs), Easing = Motion.Linear },
            new CornerRadiusTransition { Property = Border.CornerRadiusProperty, Duration = d, Easing = Motion.PointToPoint },
            new ThicknessTransition { Property = Border.BorderThicknessProperty, Duration = d, Easing = Motion.PointToPoint },
            new BoxShadowsTransition { Property = Border.BoxShadowProperty, Duration = d, Easing = Motion.PointToPoint },
        };
    }

    public static void WireOpacity(Visual target, int ms = Motion.FadeMs)
    {
        target.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(Math.Max(Motion.FadeMs, Math.Min(ms, Motion.FastMs))),
                Easing = Motion.Linear
            }
        };
    }

    public static void WireProgress(ProgressBar bar)
    {
        bar.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = RangeBase.ValueProperty,
                Duration = TimeSpan.FromMilliseconds(Motion.NormalMs),
                Easing = Motion.PointToPoint
            }
        };
    }

    public static void FastInvoke(Visual target)
    {
        try
        {
            var cv = ElementComposition.GetElementVisual(target);
            if (cv is null) return;
            var anim = cv.Compositor.CreateVector3KeyFrameAnimation();
            anim.InsertKeyFrame(0f, new Vector3(0.98f, 0.98f, 1f));
            anim.InsertKeyFrame(1f, Vector3.One);
            anim.Duration = TimeSpan.FromMilliseconds(Motion.InvokeMs);
            cv.StartAnimation("Scale", anim);
        }
        catch
        {
        }
    }

    public static async void Breathe(Control glow)
    {
        var anim = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(Motion.BreatheMs),
            IterationCount = new IterationCount(1),
            Easing = Motion.SoftOut,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0.16) } },
                new KeyFrame { Cue = new Cue(0.5), Setters = { new Setter(Visual.OpacityProperty, 0.40) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 0.22) } },
            }
        };
        try { await anim.RunAsync(glow); } catch (Exception ex) { IslandLog.Write("anim", ex.Message); }
    }

    public static async void TickFade(Control target)
    {
        WireOpacity(target, Motion.FadeMs);
        var o = target.Opacity;
        target.Opacity = Math.Max(0.55, o * 0.75);
        await Task.Delay(Motion.FadeMs);
        target.Opacity = o <= 0 ? 1 : o;
    }

    public static async void CrossfadeIcon(Visual icon, Action apply)
    {
        WireOpacity(icon, Motion.FadeMs);
        icon.Opacity = 0;
        await Task.Delay(Motion.FadeMs);
        apply();
        icon.Opacity = 1;
    }

    public static void PopChat(Visual target)
    {
        try
        {
            var cv = ElementComposition.GetElementVisual(target);
            if (cv is null) return;
            var anim = cv.Compositor.CreateVector3KeyFrameAnimation();
            anim.InsertKeyFrame(0f, new Vector3(0.86f, 0.86f, 1f));
            anim.InsertKeyFrame(1f, Vector3.One);
            anim.Duration = TimeSpan.FromMilliseconds(Motion.FastMs);
            cv.StartAnimation("Scale", anim);
        }
        catch
        {
        }
    }

    public static void SlideFromBadge(Visual target)
    {
        try
        {
            var cv = ElementComposition.GetElementVisual(target);
            if (cv is null) return;
            var anim = cv.Compositor.CreateVector3KeyFrameAnimation();
            anim.InsertKeyFrame(0f, new Vector3(-20f, 0f, 0f));
            anim.InsertKeyFrame(1f, Vector3.Zero);
            anim.Duration = TimeSpan.FromMilliseconds(Motion.FastMs);
            cv.StartAnimation("Offset", anim);
            var fade = cv.Compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0f, 0f);
            fade.InsertKeyFrame(1f, 1f);
            fade.Duration = TimeSpan.FromMilliseconds(Motion.FadeMs);
            cv.StartAnimation("Opacity", fade);
        }
        catch
        {
        }
    }

    public static void WarnFlash(Control target)
    {
        TickFade(target);
        FastInvoke(target);
    }
}

internal sealed class HwndMorph
{
    private readonly Window _window;
    private readonly Stopwatch _clock = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private Control? _pill;
    private double _w0, _w1, _h0, _h1, _hostW, _hostH;
    private int _dur;
    private long _lastMs;
    private int _frames, _dropped;
    private double _dtMin = 999, _dtMax, _dtSum;
    private bool _running;
    private bool _hwndStart, _hwndEnd, _innerLayout, _collapse;
    private int _restarts;

    public bool IsRunning => _running;
    public static string LastTiming { get; private set; } = "";
    public event Action? Completed;

    public HwndMorph(Window window) => _window = window;

    public void To(Control pill, double width, double height, int ms)
        => To(pill, null, width, height, ms);

    public async void To(Control pill, Visual? fadeContent, double width, double height, int ms, bool towardBadge = false)
    {
        var collapse = width + 4 < Math.Max(_window.Width, _w1 > 0 ? _w1 : _window.Width);
        if (_running && Math.Abs(width - _w1) < 8 && Math.Abs(height - _h1) < 2)
        {
            _restarts++;
            Append("coalesce skip target=" + width.ToString("0", CultureInfo.InvariantCulture));
            return;
        }
        if (_running) _restarts++;
        _pill = pill;
        _w0 = _window.Width;
        _h0 = _window.Height;
        _w1 = width;
        _h1 = height;
        _hostW = Math.Max(_w0, _w1);
        _hostH = Math.Max(_h0, _h1);
        _dur = Math.Max(16, ms);
        _collapse = collapse;
        _frames = 0;
        _dropped = 0;
        _dtMin = 999;
        _dtMax = 0;
        _dtSum = 0;
        _hwndStart = !collapse;
        _hwndEnd = false;
        _innerLayout = true;
        _lastMs = 0;
        _running = true;
        if (!collapse)
        {
            _window.Width = _hostW;
            _window.Height = _hostH;
            _window.Position = OverlayPlacement.Compute(_window, _hostW, _hostH);
        }
        pill.Width = _w0;
        pill.Height = _h0;
        pill.RenderTransform = null;
        if (collapse && fadeContent is not null)
        {
            IslandAnimator.WireOpacity(fadeContent, Motion.FadeMs);
            fadeContent.Opacity = 0;
            try { await Task.Delay(Motion.FadeMs); } catch (Exception ex) { IslandLog.Write("anim", ex.Message); }
            if (!_running) return;
        }
        _clock.Restart();
        Pump();
    }

    private void Pump()
    {
        if (!_running) return;
        var top = TopLevel.GetTopLevel(_window);
        if (top is not null)
        {
            top.RequestAnimationFrame(_ =>
            {
                Step();
                if (_running) Pump();
            });
            return;
        }
        Dispatcher.UIThread.Post(() =>
        {
            Step();
            if (_running) Pump();
        }, DispatcherPriority.Render);
    }

    private void Step()
    {
        if (!_running) return;
        var now = _clock.ElapsedMilliseconds;
        if (_frames > 0)
        {
            var dt = now - _lastMs;
            if (dt < 12 && now < _dur)
                return;
            _dtMin = Math.Min(_dtMin, dt);
            _dtMax = Math.Max(_dtMax, dt);
            _dtSum += dt;
            if (dt > 20.7) _dropped++;
        }
        _lastMs = now;
        _frames++;
        var raw = Math.Min(1, now / (double)_dur);
        var eased = _collapse
            ? Motion.SoftOut.Ease(raw)
            : Motion.PointToPoint.Ease(raw);
        var visW = _w0 + (_w1 - _w0) * eased;
        if (_pill is not null)
        {
            _pill.Width = visW;
            _pill.Height = _h0 + (_h1 - _h0) * eased;
            if (_pill is Border pb)
                pb.CornerRadius = new CornerRadius(Math.Max(12, _pill.Height / 2));
        }
        if (raw < 1) return;
        _running = false;
        _clock.Stop();
        _hwndEnd = true;
        _window.Width = _w1;
        _window.Height = _h1;
        _window.Position = OverlayPlacement.Compute(_window, _w1, _h1);
        if (_pill is not null)
        {
            _pill.Width = _w1;
            _pill.Height = _h1;
            _pill.RenderTransform = null;
        }
        Win32Overlay.ApplyNoActivate(_window);
        var avg = _frames > 1 ? _dtSum / (_frames - 1) : 0;
        LastTiming = string.Format(CultureInfo.InvariantCulture,
            "kind={0} path=resizeHost frames={1} avgDt={2:0.0}ms min={3:0.0} max={4:0.0} dropped={5} dur={6}ms dW={7:0} hwndStart={8} hwndEnd={9} innerScale={10} fadeFirst={11} ease={12} restarts={13} comp={14} target=16.7ms",
            _collapse ? "collapse" : "expand", _frames, avg, _dtMin >= 999 ? 0 : _dtMin, _dtMax, _dropped, _dur,
            _w1 - _w0, _hwndStart, _hwndEnd, _innerLayout, _collapse, _collapse ? "SoftOut" : "PointToPoint",
            _restarts, Program.CompositionLabel);
        Append(LastTiming);
        Completed?.Invoke();
    }

    public static string LogPath => Path.Combine(Path.GetTempPath(), "notifyisland-morph.log");

    internal static void LogLine(string line) => Append(line);

    private static void Append(string line)
    {
        try
        {
            File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss.fff ", CultureInfo.InvariantCulture) + line + Environment.NewLine);
        }
        catch
        {
        }
    }
}

internal sealed class FixedHostMorph
{
    private readonly Window _window;
    private readonly Stopwatch _clock = new();
    private bool _running;
    private int _frames, _dropped, _dumpI;
    private double _dtMin = 999, _dtMax, _dtSum;
    private long _lastMs;
    private int _dur;

    public bool IsRunning => _running;
    public static string LastTiming { get; private set; } = "";
    public event Action? Completed;

    public FixedHostMorph(Window window) => _window = window;

    public void To(Control pill, Visual? fade, double fromW, double toW, double toH, int ms, bool collapse, double originX = 0.5)
    {
        _ = Run(pill, fade, fromW, toW, toH, ms, collapse);
    }

    private async System.Threading.Tasks.Task Run(Control pill, Visual? fade, double fromW, double toW, double toH, int ms, bool collapse)
    {
        _running = true;
        _frames = 0;
        _dropped = 0;
        _dtMin = 999;
        _dtMax = 0;
        _dtSum = 0;
        _dur = Math.Max(16, ms);
        ClearScale(pill);
        if (pill is Border b0)
            ApplyGeom(b0, fromW, toH);
        if (collapse && fade is not null)
        {
            IslandAnimator.WireOpacity(fade, Motion.FadeMs);
            fade.Opacity = 0;
            await System.Threading.Tasks.Task.Delay(Motion.FadeMs);
        }
        _clock.Restart();
        _lastMs = 0;
        while (_running)
        {
            await NextFrame();
            if (!_running) break;
            var now = _clock.ElapsedMilliseconds;
            if (_frames > 0)
            {
                var dt = now - _lastMs;
                _dtMin = Math.Min(_dtMin, dt);
                _dtMax = Math.Max(_dtMax, dt);
                _dtSum += dt;
                if (dt > 20.7) _dropped++;
            }
            _lastMs = now;
            _frames++;
            var raw = Math.Min(1, now / (double)_dur);
            var eased = collapse ? Motion.SoftOut.Ease(raw) : Motion.PointToPoint.Ease(raw);
            var w = fromW + (toW - fromW) * eased;
            if (pill is Border b)
                ApplyGeom(b, w, toH);
            Dump(pill, w);
            if (raw >= 1) break;
        }
        if (pill is Border b1)
            ApplyGeom(b1, toW, toH);
        ClearScale(pill);
        _running = false;
        var avg = _frames > 1 ? _dtSum / (_frames - 1) : 0;
        LastTiming = string.Format(CultureInfo.InvariantCulture,
            "kind={0} path=fixedHost geom=widthClip scale=False frames={1} avgDt={2:0.0}ms min={3:0.0} max={4:0.0} dropped={5} dur={6}ms dW={7:0} hwndResize=False hwndMove=False settleRgn=True ease={8} comp={9} target=16.7ms",
            collapse ? "collapse" : "expand", _frames, avg, _dtMin >= 999 ? 0 : _dtMin, _dtMax, _dropped, _dur,
            toW - fromW, collapse ? "SoftOut" : "PointToPoint", Program.CompositionLabel);
        HwndMorph.LogLine(LastTiming);
        Completed?.Invoke();
    }

    private void ApplyGeom(Border pill, double w, double h)
    {
        w = Math.Max(32, w);
        h = Math.Max(24, h);
        var r = h / 2;
        pill.Width = w;
        pill.Height = h;
        pill.CornerRadius = new CornerRadius(r);
        pill.ClipToBounds = true;
        pill.Clip = null;
        pill.RenderTransform = null;
        if (_window is OverlayWindow ov)
            ov.PlacePillInsideStrip(w, h);
    }

    private static void ClearScale(Control pill)
    {
        pill.RenderTransform = null;
        try
        {
            var cv = ElementComposition.GetElementVisual(pill);
            if (cv is not null) cv.Scale = Vector3.One;
        }
        catch
        {
        }
    }

    private System.Threading.Tasks.Task NextFrame()
    {
        var tcs = new System.Threading.Tasks.TaskCompletionSource(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        var top = TopLevel.GetTopLevel(_window);
        if (top is null)
        {
            Dispatcher.UIThread.Post(() => tcs.TrySetResult(), DispatcherPriority.Render);
            return tcs.Task;
        }
        top.RequestAnimationFrame(_ => tcs.TrySetResult());
        return tcs.Task;
    }

    private void Dump(Control pill, double w)
    {
        var dir = Program.DumpFramesDir;
        if (string.IsNullOrWhiteSpace(dir) || _dumpI > 240) return;
        try
        {
            Directory.CreateDirectory(dir);
            var pw = Math.Max(1, (int)Math.Ceiling(pill.Bounds.Width > 1 ? pill.Bounds.Width : w));
            var ph = Math.Max(1, (int)Math.Ceiling(pill.Bounds.Height > 1 ? pill.Bounds.Height : 40));
            using var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(pw, ph), new Avalonia.Vector(96, 96));
            bmp.Render(pill);
            bmp.Save(Path.Combine(dir, "p" + _dumpI.ToString("0000", CultureInfo.InvariantCulture) + ".png"));
            File.AppendAllText(Path.Combine(dir, "geom.txt"),
                _dumpI.ToString(CultureInfo.InvariantCulture) + " w=" + w.ToString("0.0", CultureInfo.InvariantCulture) +
                " bounds=" + pill.Bounds + Environment.NewLine);
            _dumpI++;
        }
        catch
        {
        }
    }
}
