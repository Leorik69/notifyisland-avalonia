using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

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
}
