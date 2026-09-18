using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace NotifyIsland;

internal static class IslandAnimator
{
    public static void WireLayout(Control target, int ms)
    {
        var d = TimeSpan.FromMilliseconds(Math.Max(120, ms));
        var ease = new CubicEaseOut();
        target.Transitions = new Transitions
        {
            new DoubleTransition { Property = Layoutable.WidthProperty, Duration = d, Easing = ease },
            new DoubleTransition { Property = Layoutable.HeightProperty, Duration = d, Easing = ease },
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(Motion.FadeMs), Easing = ease },
            new CornerRadiusTransition { Property = Border.CornerRadiusProperty, Duration = d, Easing = ease },
            new ThicknessTransition { Property = Border.BorderThicknessProperty, Duration = d, Easing = ease },
            new BoxShadowsTransition { Property = Border.BoxShadowProperty, Duration = d, Easing = ease },
        };
    }

    public static void WireOpacity(Visual target, int ms)
    {
        target.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(Math.Max(120, ms)),
                Easing = new CubicEaseOut()
            }
        };
    }

    public static void WireProgress(ProgressBar bar, int ms)
    {
        bar.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = RangeBase.ValueProperty,
                Duration = TimeSpan.FromMilliseconds(Math.Max(120, ms)),
                Easing = new CubicEaseOut()
            }
        };
    }

    public static async void Pulse(Control target, int ms)
    {
        EnsureScale(target);
        var anim = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(Math.Clamp(ms, 160, 280)),
            Easing = new CubicEaseOut(),
            Children =
            {
                ScaleKf(0, 1),
                ScaleKf(0.4, 1.04),
                ScaleKf(1, 1)
            }
        };
        try { await anim.RunAsync(target); } catch { }
    }

    public static async void Breathe(Control glow, int ms)
    {
        var anim = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(Math.Clamp(ms, 600, 1100)),
            IterationCount = new IterationCount(2),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0.16) } },
                new KeyFrame { Cue = new Cue(0.5), Setters = { new Setter(Visual.OpacityProperty, 0.48) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 0.20) } },
            }
        };
        try { await anim.RunAsync(glow); } catch { }
    }

    public static async void TickPop(Control target)
    {
        EnsureScale(target);
        var anim = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(Motion.PulseMs),
            Easing = new CubicEaseOut(),
            Children =
            {
                ScaleKf(0, 1),
                ScaleKf(0.35, 1.08),
                ScaleKf(1, 1)
            }
        };
        try { await anim.RunAsync(target); } catch { }
    }

    private static KeyFrame ScaleKf(double cue, double s) => new()
    {
        Cue = new Cue(cue),
        Setters =
        {
            new Setter(ScaleTransform.ScaleXProperty, s),
            new Setter(ScaleTransform.ScaleYProperty, s)
        }
    };

    private static void EnsureScale(Control target)
    {
        if (target.RenderTransform is not ScaleTransform)
            target.RenderTransform = new ScaleTransform(1, 1);
        target.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
    }

    public static async void CrossfadeIcon(Visual icon, Action apply)
    {
        var fade = Math.Max(80, Motion.FadeMs / 2);
        WireOpacity(icon, fade);
        icon.Opacity = 0;
        await Task.Delay(fade);
        apply();
        icon.Opacity = 1;
    }
}

internal sealed class HwndMorph
{
    private readonly Window _window;
    private readonly DispatcherTimer _timer;
    private double _w0, _w1, _h0, _h1;
    private int _x0, _x1, _y;
    private int _elapsed, _dur;

    public HwndMorph(Window window)
    {
        _window = window;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Step();
    }

    public void To(double width, double height, int ms)
    {
        var screen = _window.Screens.Primary ?? _window.Screens.ScreenFromWindow(_window);
        if (screen is null)
        {
            _window.Width = width; _window.Height = height;
            return;
        }
        var wa = screen.WorkingArea;
        var scale = _window.RenderScaling;
        var toX = wa.X + (wa.Width - (int)Math.Round(width * scale)) / 2;
        var y = wa.Y + 8;
        _w0 = _window.Width; _h0 = _window.Height;
        _w1 = width; _h1 = height;
        _x0 = _window.Position.X; _x1 = toX; _y = y;
        _elapsed = 0;
        _dur = Math.Max(16, ms);
        _timer.Start();
    }

    private void Step()
    {
        _elapsed += 16;
        var t = Math.Min(1, _elapsed / (double)_dur);
        t = 1 - Math.Pow(1 - t, 3);
        _window.Width = _w0 + (_w1 - _w0) * t;
        _window.Height = _h0 + (_h1 - _h0) * t;
        _window.Position = new PixelPoint((int)Math.Round(_x0 + (_x1 - _x0) * t), _y);
        Win32Overlay.ApplyNoActivate(_window);
        if (t >= 1)
        {
            _timer.Stop();
            _window.Width = _w1;
            _window.Height = _h1;
            _window.Position = new PixelPoint(_x1, _y);
        }
    }
}
