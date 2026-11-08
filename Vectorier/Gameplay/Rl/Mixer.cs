#nullable enable
using System;
using System.Collections.Generic;

namespace Vectorier.Gameplay.Audio;

/// <summary>
/// Mixes the playing sounds (one voice per sound: playing it again restarts it, as in Raylib) and the music
/// (looping). Called from the output's thread; the game changes it from the UI thread.
/// </summary>
public sealed class Mixer
{
    public const int SampleRate = 44100;

    private sealed class Voice
    {
        public SoundData Data = null!;
        public int Position;
        public bool Loop;
        public bool Paused;
        public bool IsMusic;
    }

    private readonly object _lock = new();
    private readonly List<Voice> _voices = new();

    public float MasterVolume { get; set; } = 1;

    public void Play(SoundData data)
    {
        lock (_lock)
        {
            var voice = Find(data, false);
            if (voice == null)
                _voices.Add(voice = new Voice { Data = data });
            voice.Position = 0;
            voice.Paused = false;
        }
    }

    public void Stop(SoundData data)
    {
        lock (_lock) _voices.RemoveAll(v => v.Data == data && !v.IsMusic);
    }

    public bool IsPlaying(SoundData data)
    {
        lock (_lock) return Find(data, false) != null;
    }

    public void PlayMusic(SoundData data, bool loop)
    {
        lock (_lock)
        {
            var voice = Find(data, true);
            if (voice == null)
                _voices.Add(voice = new Voice { Data = data, IsMusic = true });
            voice.Loop = loop;
            voice.Paused = false;
        }
    }

    public void StopMusic(SoundData data)
    {
        lock (_lock) _voices.RemoveAll(v => v.Data == data && v.IsMusic);
    }

    public bool IsMusicPlaying(SoundData data)
    {
        lock (_lock) return Find(data, true) is { Paused: false };
    }

    /// <summary>Everything stops (the game was left).</summary>
    public void StopAll()
    {
        lock (_lock) _voices.Clear();
    }

    private Voice? Find(SoundData data, bool music)
    {
        foreach (var v in _voices)
            if (v.Data == data && v.IsMusic == music)
                return v;
        return null;
    }

    /// <summary>Fills <paramref name="buffer"/> (interleaved stereo floats) with the next frames.</summary>
    public void Render(float[] buffer, int frames)
    {
        Array.Clear(buffer, 0, frames * 2);
        lock (_lock)
        {
            float master = Math.Clamp(MasterVolume, 0, 1);
            for (int v = _voices.Count - 1; v >= 0; v--)
            {
                var voice = _voices[v];
                if (voice.Paused)
                    continue;
                var samples = voice.Data.Samples;
                int total = voice.Data.FrameCount;
                float gain = voice.Data.Volume * master;
                int pos = voice.Position;
                for (int f = 0; f < frames; f++)
                {
                    if (pos >= total)
                    {
                        if (!voice.Loop || total == 0)
                            break;
                        pos = 0;
                    }
                    buffer[f * 2] += samples[pos * 2] * gain;
                    buffer[f * 2 + 1] += samples[pos * 2 + 1] * gain;
                    pos++;
                }
                voice.Position = pos;
                if (pos >= total && !voice.Loop)
                    _voices.RemoveAt(v);
            }
        }
        for (int i = 0; i < frames * 2; i++)
            buffer[i] = Math.Clamp(buffer[i], -1f, 1f);
    }

    /// <summary>The same as <see cref="Render(float[], int)"/>, as 16-bit samples (most outputs take these).</summary>
    public void Render(short[] buffer, int frames, ref float[]? scratch)
    {
        if (scratch == null || scratch.Length < frames * 2)
            scratch = new float[frames * 2];
        Render(scratch, frames);
        for (int i = 0; i < frames * 2; i++)
            buffer[i] = (short)(scratch[i] * 32767);
    }
}
