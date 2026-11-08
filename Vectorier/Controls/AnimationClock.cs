using System;
using System.Diagnostics;
using Avalonia.Threading;
using Vectorier.Services.Assets;

namespace Vectorier.Controls;

/// <summary>Shared 20 FPS clock for animated previews. Runs only while something listens.</summary>
public static class AnimationClock
{
    private static readonly Stopwatch Watch = Stopwatch.StartNew();
    private static DispatcherTimer? _timer;
    private static int _subscribers;
    private static event Action? TickInternal;

    public static double Seconds => Watch.Elapsed.TotalSeconds;

    public static event Action? Tick
    {
        add
        {
            TickInternal += value;
            _subscribers++;
            if (_timer == null)
            {
                _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(1000.0 / TextureAsset.FramesPerSecond), DispatcherPriority.Background, (_, _) => TickInternal?.Invoke());
            }
            _timer.Start();
        }
        remove
        {
            TickInternal -= value;
            _subscribers = Math.Max(0, _subscribers - 1);
            if (_subscribers == 0)
                _timer?.Stop();
        }
    }
}
