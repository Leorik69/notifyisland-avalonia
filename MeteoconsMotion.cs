using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace NotifyIsland;

/// <summary>
/// Avalonia.Svg.Skia does not execute SMIL (&lt;animate&gt; / &lt;animateTransform&gt;).
/// Meteocons SVGs keep SMIL for browsers; here we mirror the primary motion with Avalonia animations
/// (rotate for clear/partly, soft bob for clouds/precip, opacity pulse for storm/fog).
/// </summary>
public static class MeteoconsMotion
{
    public static Avalonia.Controls.Control Wrap(Avalonia.Controls.Control icon, string key, double size)
    {
        var host = new Border
        {
            Width = size,
            Height = size,
            Child = icon,
            ClipToBounds = false,
            IsHitTestVisible = false,
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative)
        };

        var rotate = new RotateTransform();
        var translate = new TranslateTransform();
        host.RenderTransform = new TransformGroup { Children = { rotate, translate } };

        host.AttachedToVisualTree += (_, _) => _ = RunAsync(host, rotate, translate, key);
        return host;
    }

    private static async Task RunAsync(Avalonia.Controls.Control host, RotateTransform rotate, TranslateTransform translate, string key)
    {
        try
        {
            var k = key.Trim().ToLowerInvariant();
            if (k is "weather-clear" or "weather-partly")
            {
                var spin = new Animation
                {
                    Duration = TimeSpan.FromSeconds(k == "weather-clear" ? 6 : 10),
                    IterationCount = IterationCount.Infinite,
                    Easing = new LinearEasing(),
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(RotateTransform.AngleProperty, 0d) } },
                        new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(RotateTransform.AngleProperty, 360d) } }
                    }
                };
                await spin.RunAsync(rotate);
                return;
            }

            if (k is "weather-cloud" or "weather-drizzle" or "weather-rain" or "weather-snow" or "weather-sleet")
            {
                var bob = new Animation
                {
                    Duration = TimeSpan.FromSeconds(3),
                    IterationCount = IterationCount.Infinite,
                    Easing = new SineEaseInOut(),
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(TranslateTransform.YProperty, 0d) } },
                        new KeyFrame { Cue = new Cue(0.5d), Setters = { new Setter(TranslateTransform.YProperty, -2.5d) } },
                        new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(TranslateTransform.YProperty, 0d) } }
                    }
                };
                await bob.RunAsync(translate);
                return;
            }

            if (k is "weather-storm" or "weather-fog")
            {
                var pulse = new Animation
                {
                    Duration = TimeSpan.FromSeconds(k == "weather-storm" ? 1.2 : 2.4),
                    IterationCount = IterationCount.Infinite,
                    Easing = new SineEaseInOut(),
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(Visual.OpacityProperty, 1.0) } },
                        new KeyFrame { Cue = new Cue(0.5d), Setters = { new Setter(Visual.OpacityProperty, k == "weather-storm" ? 0.55 : 0.72) } },
                        new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(Visual.OpacityProperty, 1.0) } }
                    }
                };
                await pulse.RunAsync(host);
            }
        }
        catch
        {
            // Animation cancelled when control detaches — ignore.
        }
    }
}
