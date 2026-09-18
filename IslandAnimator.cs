using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
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
        try { await anim.RunAsync(glow); } catch { }
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
}

internal sealed class HwndMorph
{
    private readonly Window _window;
    private readonly Stopwatch _clock = new();
    private double _w0, _w1, _h0, _h1;
    private int _x0, _x1, _y0, _y1;
    private int _dur;
    private long _lastMs;
    private int _frames;
    private double _dtMin = 999, _dtMax, _dtSum;
    private bool _running;

    public static string LastTiming { get; private set; } = "";

    public HwndMorph(Window window) => _window = window;

    public void To(double width, double height, int ms)
    {
        var dest = OverlayPlacement.Compute(_window, width, height);
        _w0 = _window.Width; _h0 = _window.Height;
        _w1 = width; _h1 = height;
        _x0 = _window.Position.X; _x1 = dest.X;
        _y0 = _window.Position.Y; _y1 = dest.Y;
        _dur = Math.Max(16, ms);
        _frames = 0;
        _dtMin = 999; _dtMax = 0; _dtSum = 0;
        _clock.Restart();
        _lastMs = 0;
        _running = true;
        ScheduleNext();
    }

    private void ScheduleNext()
    {
        var now = _clock.ElapsedMilliseconds;
        var due = (long)(_frames * 16.7);
        var wait = (int)Math.Clamp(due - now, 1, 16);
        Dispatcher.UIThread.Post(async () =>
        {
            await Task.Delay(wait).ConfigureAwait(true);
            Step();
        }, DispatcherPriority.Render);
    }

    private void Step()
    {
        if (!_running) return;
        var now = _clock.ElapsedMilliseconds;
        if (_frames > 0)
        {
            var dt = now - _lastMs;
            _dtMin = Math.Min(_dtMin, dt);
            _dtMax = Math.Max(_dtMax, dt);
            _dtSum += dt;
        }
        _lastMs = now;
        _frames++;
        var t = Math.Min(1, now / (double)_dur);
        t = EasePointToPoint(t);
        _window.Width = _w0 + (_w1 - _w0) * t;
        _window.Height = _h0 + (_h1 - _h0) * t;
        _window.Position = new PixelPoint(
            (int)Math.Round(_x0 + (_x1 - _x0) * t),
            (int)Math.Round(_y0 + (_y1 - _y0) * t));
        if (t >= 1)
        {
            _running = false;
            _clock.Stop();
            _window.Width = _w1;
            _window.Height = _h1;
            _window.Position = new PixelPoint(_x1, _y1);
            Win32Overlay.ApplyNoActivate(_window);
            var avg = _frames > 1 ? _dtSum / (_frames - 1) : 0;
            LastTiming = string.Format(CultureInfo.InvariantCulture,
                "frames={0} avgDt={1:0.0}ms min={2:0.0} max={3:0.0} dur={4}ms target=16.7ms",
                _frames, avg, _dtMin >= 999 ? 0 : _dtMin, _dtMax, _dur);
            try
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "notifyisland-morph.log"), LastTiming + Environment.NewLine);
            }
            catch
            {
            }
            return;
        }
        ScheduleNext();
    }

    private static double EasePointToPoint(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Motion.PointToPoint.Ease(t);
    }
}
