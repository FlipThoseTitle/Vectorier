using System;
using System.Diagnostics;
using Avalonia.Threading;

namespace Vectorier.Controls.Scene;

/// <summary>
/// The Scene redraws at most 60 times a second: requests that arrive sooner (pointer moves, playback ticks)
/// are merged into one redraw at the next 1/60 s slot. The editor is not a game, so rendering faster than that
/// only costs CPU and GPU time.
/// </summary>
public partial class SceneView
{
    public const double MaxFramesPerSecond = 60;
    private static readonly long FrameTicks = (long)(Stopwatch.Frequency / MaxFramesPerSecond);

    private long _lastRenderTimestamp;
    private bool _redrawScheduled;
    private long _scheduledAt;

    /// <summary>Throttled replacement of <see cref="Avalonia.Visual.InvalidateVisual"/> for everything in the Scene.</summary>
    public new void InvalidateVisual()
    {
        long now = Stopwatch.GetTimestamp();
        // A request that never turned into a frame (view hidden meanwhile) doesn't block later ones.
        if (_redrawScheduled && now - _scheduledAt < FrameTicks * 6)
            return;
        _scheduledAt = now;
        long elapsed = now - _lastRenderTimestamp;
        if (elapsed >= FrameTicks)
        {
            _redrawScheduled = true;
            base.InvalidateVisual();
            return;
        }
        _redrawScheduled = true;
        var wait = TimeSpan.FromSeconds((double)(FrameTicks - elapsed) / Stopwatch.Frequency);
        DispatcherTimer.RunOnce(() => base.InvalidateVisual(), wait, DispatcherPriority.Render);
    }

    /// <summary>Called at the start of every frame.</summary>
    private void MarkFrameRendered()
    {
        _lastRenderTimestamp = Stopwatch.GetTimestamp();
        _redrawScheduled = false;
    }
}
