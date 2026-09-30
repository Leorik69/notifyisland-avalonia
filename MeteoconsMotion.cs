using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace NotifyIsland;

/// <summary>
/// Avalonia.Svg.Skia does not execute SMIL (&lt;animate&gt; / &lt;animateTransform&gt;).
/// Meteocons SVGs keep SMIL for browsers; here we mirror the primary motion with a per-frame
/// tick (rotate for clear/partly, soft bob for clouds/precip, opacity pulse for storm/fog).
///
/// Why a tick and not <c>Avalonia.Animation.Animation</c>: in Avalonia 11.3 the only public
/// <c>RunAsync</c> is <c>RunAsync(Animatable, CancellationToken)</c> — the argument is the control
/// to animate, NOT the transform to drive. So <c>anim.RunAsync(_pillScale)</c> type-checks
/// (a ScaleTransform is an Animatable) and then throws
/// <c>InvalidCastException: ScaleTransform → Visual</c> from inside Avalonia, which is what killed
/// the process on every single pill click. Worse, that same overload sets
/// «Looping animations must not use the Run method.» on the returned Task for every
/// <c>IterationCount.Infinite</c> animation, so all three weather loops were not just throwing
/// away their motion — they were faulting a Task nobody awaits, which is the permanent
/// unobserved-task-exception line in the log.
///
/// The frame maths itself is <see cref="MeteoconsMotionTrack"/> in Core: pure, testable without a
/// window, and the same easing dictionary the rest of the 1.12.4 layer uses.
/// </summary>
public static class MeteoconsMotion
{
    /// <summary>
    /// Reduced-motion gate. When <c>true</c> every live tick is suppressed and every visible
    /// host lands on its neutral pose (no rotation, no bob, full opacity). <see cref="OverlayWindow.ApplyAnimationSettings"/>
    /// pushes this every time settings or the OS switch change, and a single static flag is
    /// enough because the weather surface keeps at most two of these hosts alive.
    /// </summary>
    private static bool _reduced;
    private static readonly HashSet<Motion> _live = new();

    /// <summary>
    /// Put a Meteocons icon inside a host border that carries the mirrored movement.
    ///
    /// The host owns the transform (its origin is already pinned to the centre) and the tick
    /// lifecycle: it starts when the host joins the visual tree and stops when the host leaves
    /// it. The stop is not an optimisation — an Avalonia <c>Animation</c> run with
    /// <c>IterationCount.Infinite</c> and no cancellation outlives the icon it was animating, so
    /// a hidden weather slot would keep spinning (and keep a clock) for the life of the process.
    /// </summary>
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
        var group = new TransformGroup { Children = { rotate, translate } };
        host.RenderTransform = group;

        var kind = MeteoconsMotionTrack.KindFor(key);
        if (kind != MeteoconsMotionKind.None)
        {
            var motion = new Motion(host, rotate, translate, kind, key);
            host.AttachedToVisualTree += motion.OnAttached;
            host.DetachedFromVisualTree += motion.OnDetached;
        }

        return host;
    }

    /// <summary>
    /// Push the reduced-motion flag. Called from <see cref="OverlayWindow.ApplyAnimationSettings"/>
    /// on settings change so a live icon host can stop or restart without rebuilding the icon.
    /// </summary>
    public static void SetReducedMotion(bool reduced)
    {
        if (_reduced == reduced) return;
        _reduced = reduced;
        foreach (var motion in _live)
            motion.ApplyReducedMotion(reduced);
    }

    /// <summary>
    /// The tick for one icon host. One <see cref="DispatcherTimer"/> per host, and it only runs
    /// between attach and detach: the weather surface keeps at most two of these (the crossfade
    /// pair), and neither is on screen outside the weather slot.
    /// </summary>
    private sealed class Motion
    {
        private readonly Avalonia.Controls.Control _host;
        private readonly RotateTransform _rotate;
        private readonly TranslateTransform _translate;
        private readonly MeteoconsMotionKind _kind;
        private readonly string _key;
        private readonly int _periodMs;
        private readonly DispatcherTimer _timer;
        // Wall clock, not an accumulator of tick intervals: DispatcherTimer intervals are a
        // request, not a contract, and an accumulator drifts visibly on a machine that drops a
        // frame — which on a 3 s bob is a cloud that visibly skips.
        private readonly Stopwatch _watch = new();
        private bool _running;

        public Motion(Avalonia.Controls.Control host, RotateTransform rotate, TranslateTransform translate,
                       MeteoconsMotionKind kind, string key)
        {
            _host = host;
            _rotate = rotate;
            _translate = translate;
            _kind = kind;
            _key = key;
            _periodMs = MeteoconsMotionTrack.PeriodMsFor(key);
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(OverlayTokens.MeteoconsTickMs)
            };
            _timer.Tick += OnTick;
        }

        internal void OnAttached(object? sender, EventArgs e)
        {
            if (_running) return;
            _running = true;
            _live.Add(this);
            // Restart at 0 on every attach rather than resuming: a host that was off the tree for
            // a minute must come back at the start of a cycle, not at whatever phase it was
            // frozen in — that freeze is the visible "stuck icon" the infinite Animation used to
            // produce.
            _watch.Restart();
            // Apply frame 0 straight away, otherwise the icon shows its pre-motion pose for one
            // whole interval (33 ms of visible "no animation") on every attach.
            Apply(MeteoconsMotionTrack.FrameAt(_kind, _key, _periodMs, 0));
            if (_reduced) ApplyReducedPose();
            else _timer.Start();
        }

        internal void OnDetached(object? sender, EventArgs e)
        {
            if (!_running) return;
            _running = false;
            _live.Remove(this);
            _timer.Stop();
            _watch.Reset();
            ApplyNeutralPose();
        }

        /// <summary>
        /// One-shot gate from the static <see cref="SetReducedMotion"/>. Stops the timer and
        /// freezes the neutral pose; restart-from-zero happens on the next attach, which is what
        /// the user expects when they flip the OS switch — the icon stops moving without
        /// abruptly snapping through a half-finished frame.
        /// </summary>
        internal void ApplyReducedMotion(bool reduced)
        {
            if (reduced)
            {
                if (_timer.IsEnabled) _timer.Stop();
                ApplyReducedPose();
            }
            else if (_running && !_timer.IsEnabled)
            {
                _watch.Restart();
                Apply(MeteoconsMotionTrack.FrameAt(_kind, _key, _periodMs, 0));
                _timer.Start();
            }
        }

        private void OnTick(object? sender, EventArgs e) =>
            Apply(MeteoconsMotionTrack.FrameAt(_kind, _key, _periodMs, _watch.Elapsed.TotalMilliseconds));

        private void Apply(MeteoconsFrame f)
        {
            // Each kind writes one channel and leaves the other two at rest, so the unused
            // transform never has to be reset per frame.
            switch (_kind)
            {
                case MeteoconsMotionKind.Spin:
                    _rotate.Angle = f.Angle;
                    break;
                case MeteoconsMotionKind.Bob:
                    _translate.Y = f.OffsetY;
                    break;
                case MeteoconsMotionKind.Pulse:
                    _host.Opacity = f.Opacity;
                    break;
            }
        }

        private void ApplyReducedPose() => ApplyNeutralPose();

        private void ApplyNeutralPose()
        {
            _rotate.Angle = 0;
            _translate.Y = 0;
            _host.Opacity = 1;
        }
    }
}
