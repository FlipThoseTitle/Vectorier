using System;
using Vectorier.Gameplay.Audio;

namespace Vectorier.Services.Platform.Audio;

/// <summary>
/// The game's audio on the desktop, through the system's own audio API (nothing is shipped with the editor):
/// waveOut on Windows, PulseAudio (or ALSA) on Linux, Core Audio's AudioQueue on macOS. Each pulls 16-bit stereo
/// frames from the game mixer in small blocks (about 20 ms) on its own thread.
/// </summary>
public static class DesktopAudio
{
    /// <summary>About 23 ms per block at 44.1 kHz.</summary>
    public const int BlockFrames = 1024;

    public static IAudioOutput? Create()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return new WaveOutOutput();
            if (OperatingSystem.IsMacOS())
                return new AudioQueueOutput();
            if (OperatingSystem.IsLinux())
                return LinuxOutput.Create();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Game audio: " + ex.Message);
        }
        return null;
    }
}
