using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Vectorier.Services.Platform;

/// <summary>
/// Sound previews for the desktop. Windows plays WAV files through waveOut (adjustable volume, end detection);
/// other formats and other systems use the platform's command-line player (afplay, paplay, ffplay), whose exit
/// marks the end of playback.
/// </summary>
public sealed class SoundPreview
{
    private readonly object _lock = new();
    private WaveOutPlayback? _wave;
    private Process? _process;
    private TaskCompletionSource? _done;
    private double _volume = 1;

    public Task PlayAsync(string file, double volume)
    {
        Stop();
        _volume = Math.Clamp(volume, 0, 1);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock) _done = done;

        try
        {
            if (OperatingSystem.IsWindows() && file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                var wave = WaveOutPlayback.Start(File.ReadAllBytes(file), _volume, () => Finish(done));
                if (wave != null)
                {
                    lock (_lock) _wave = wave;
                    return done.Task;
                }
            }

            var process = StartProcess(file, _volume);
            if (process == null)
            {
                done.TrySetResult();
                return done.Task;
            }
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => Finish(done);
            lock (_lock) _process = process;
            if (process.HasExited) Finish(done);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Sound preview failed: " + ex.Message);
            done.TrySetResult();
        }
        return done.Task;
    }

    private void Finish(TaskCompletionSource done)
    {
        lock (_lock)
        {
            if (ReferenceEquals(_done, done))
            {
                _done = null;
                _wave = null;
                _process = null;
            }
        }
        done.TrySetResult();
    }

    public void SetVolume(double volume)
    {
        _volume = Math.Clamp(volume, 0, 1);
        lock (_lock) _wave?.SetVolume(_volume);
    }

    public void Stop()
    {
        WaveOutPlayback? wave;
        Process? process;
        TaskCompletionSource? done;
        lock (_lock)
        {
            wave = _wave;
            process = _process;
            done = _done;
            _wave = null;
            _process = null;
            _done = null;
        }
        wave?.Stop();
        try
        {
            if (process is { HasExited: false })
                process.Kill();
        }
        catch
        {
            // already gone
        }
        done?.TrySetResult();
    }

    private static Process? StartProcess(string file, double volume)
    {
        if (OperatingSystem.IsMacOS())
            return TryStart("afplay", "-v", volume.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), file);
        if (OperatingSystem.IsWindows())
            return null; // non-WAV files: no built-in player without a window
        return TryStart("paplay", $"--volume={(int)(volume * 65536)}", file)
               ?? TryStart("ffplay", "-nodisp", "-autoexit", "-loglevel", "quiet", "-volume", ((int)(volume * 100)).ToString(), file)
               ?? TryStart("aplay", "-q", file);
    }

    private static Process? TryStart(string exe, params string[] args)
    {
        try
        {
            var info = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
            foreach (var arg in args)
                info.ArgumentList.Add(arg);
            return Process.Start(info);
        }
        catch
        {
            return null;
        }
    }
}
