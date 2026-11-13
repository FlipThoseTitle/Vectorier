// The game's audio in the editor: a small mixer (the project's sounds and music are WAV files) feeding the system's
// own output, chosen by the platform (see IPlatformServices.CreateAudioOutput): waveOut on Windows, PulseAudio / ALSA
// on Linux, Core Audio on macOS, Web Audio in the browser. No native library is shipped.
#nullable enable
using System;

namespace Vectorier.Gameplay.Audio;


/// <summary>A system audio output pulling stereo frames from the mixer (see the platform services).</summary>
public interface IAudioOutput : IDisposable
{
    void Start(Mixer mixer);
}

/// <summary>The game's audio: one mixer, started on the first game and kept while the editor runs.</summary>
public static class GameAudio
{
    public static Mixer Mixer { get; } = new();

    /// <summary>Set by the platform: the system's output (null: the game is silent).</summary>
    public static Func<IAudioOutput?>? CreateOutput { get; set; }

    private static IAudioOutput? _output;

    public static void Start()
    {
        if (_output != null)
            return;
        try
        {
            _output = CreateOutput?.Invoke();
            _output?.Start(Mixer);
        }
        catch (Exception ex)
        {
            Vectorier.Gameplay.Diagnostics.GameLog.Warning("Starting the game's audio", "The game's audio could not start (the game is silent): " + ex.Message);
            _output = null;
        }
    }

    public static void Stop()
    {
        Mixer.StopAll();
    }
}
