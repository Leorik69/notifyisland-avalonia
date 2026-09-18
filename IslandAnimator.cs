using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace NotifyIsland;

internal static class IslandAnimator
{
    public static async void Morph(Control target, double fromW, double fromH, double toW, double toH, int ms)
    {
        if (ms < 40)
        {
            target.Width = toW;
            target.Height = toH;
            return;
        }
        var anim = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(ms),
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                Kf(0, fromW, fromH),
                Kf(1, toW, toH)
            }
        };
        try { await anim.RunAsync(target); } catch { target.Width = toW; target.Height = toH; }
    }

    public static async void Pulse(Control target)
    {
        EnsureScale(target);
        var anim = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(420),
            Easing = new CubicEaseOut(),
            Children =
            {
                ScaleKf(0, 1),
                ScaleKf(0.45, 1.045),
                ScaleKf(1, 1)
            }
        };
        try { await anim.RunAsync(target); } catch { }
    }

    public static async void Breathe(Control glow)
    {
        var anim = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(1800),
            IterationCount = new IterationCount(3),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0.18) } },
                new KeyFrame { Cue = new Cue(0.5), Setters = { new Setter(Visual.OpacityProperty, 0.55) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 0.22) } },
            }
        };
        try { await anim.RunAsync(glow); } catch { }
    }

    private static KeyFrame Kf(double cue, double w, double h) => new()
    {
        Cue = new Cue(cue),
        Setters =
        {
            new Setter(Layoutable.WidthProperty, w),
            new Setter(Layoutable.HeightProperty, h)
        }
    };

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
        if (target.RenderTransform is ScaleTransform) return;
        target.RenderTransform = new ScaleTransform(1, 1);
        target.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
    }
}
