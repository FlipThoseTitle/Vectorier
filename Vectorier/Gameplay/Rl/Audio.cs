// The game's audio in the editor: a small mixer (the project's sounds and music are WAV files) feeding the system's
// own output, chosen by the platform (see IPlatformServices.CreateAudioOutput): waveOut on Windows, PulseAudio / ALSA
// on Linux, Core Audio on macOS, Web Audio in the browser. No native library is shipped.
#nullable enable
using System;
using System.Collections.Generic;

namespace Vectorier.Gameplay.Audio
{
    /// <summary>A decoded WAV: interleaved stereo floats at the mixer's rate.</summary>
    public sealed class SoundData
    {
        public SoundData(string name, float[] samples)
        {
            Name = name;
            Samples = samples;
        }

        public string Name { get; }

        /// <summary>Interleaved left / right at <see cref="Mixer.SampleRate"/>.</summary>
        public float[] Samples { get; }

        public int FrameCount => Samples.Length / 2;

        /// <summary>Raylib's SetSoundVolume / SetMusicVolume.</summary>
        public float Volume { get; set; } = 1;

        /// <summary>
        /// Reads a WAV file: PCM 8 / 16 / 24 / 32-bit or IEEE float, any rate and channel count. Resampled (linear)
        /// to the mixer's rate, mono played on both sides. Null when the file is not a WAV the mixer can play.
        /// </summary>
        public static SoundData? FromWav(string name, byte[] data)
        {
            try
            {
                if (data.Length < 12 || data[0] != 'R' || data[1] != 'I' || data[2] != 'F' || data[3] != 'F' || data[8] != 'W' || data[9] != 'A')
                    return null;
                int pos = 12, format = 0, channels = 0, rate = 0, bits = 0;
                int dataStart = -1, dataLength = 0;
                while (pos + 8 <= data.Length)
                {
                    string id = System.Text.Encoding.ASCII.GetString(data, pos, 4);
                    int size = BitConverter.ToInt32(data, pos + 4);
                    int body = pos + 8;
                    if (size < 0 || body + size > data.Length) size = data.Length - body;
                    if (id == "fmt ")
                    {
                        format = BitConverter.ToUInt16(data, body);
                        channels = BitConverter.ToUInt16(data, body + 2);
                        rate = BitConverter.ToInt32(data, body + 4);
                        bits = BitConverter.ToUInt16(data, body + 14);
                        if (format == 0xFFFE && size >= 26) // WAVE_FORMAT_EXTENSIBLE: the real format is in the GUID
                            format = BitConverter.ToUInt16(data, body + 24);
                    }
                    else if (id == "data")
                    {
                        dataStart = body;
                        dataLength = size;
                        break;
                    }
                    pos = body + size + (size & 1);
                }
                if (dataStart < 0 || channels <= 0 || rate <= 0 || (format != 1 && format != 3))
                    return null;
                int bytesPerSample = bits / 8;
                if (bytesPerSample <= 0) return null;
                int frames = dataLength / (bytesPerSample * channels);
                var source = new float[frames * 2];
                for (int f = 0; f < frames; f++)
                {
                    int at = dataStart + f * bytesPerSample * channels;
                    float left = Sample(data, at, format, bits);
                    float right = channels > 1 ? Sample(data, at + bytesPerSample, format, bits) : left;
                    source[f * 2] = left;
                    source[f * 2 + 1] = right;
                }
                return new SoundData(name, rate == Mixer.SampleRate ? source : Resample(source, rate, Mixer.SampleRate));
            }
            catch
            {
                return null;
            }
        }

        private static float Sample(byte[] d, int at, int format, int bits)
        {
            if (format == 3)
                return bits == 64 ? (float)BitConverter.ToDouble(d, at) : BitConverter.ToSingle(d, at);
            return bits switch
            {
                8 => (d[at] - 128) / 128f,
                16 => BitConverter.ToInt16(d, at) / 32768f,
                24 => ((d[at] | d[at + 1] << 8 | (sbyte)d[at + 2] << 16)) / 8388608f,
                32 => BitConverter.ToInt32(d, at) / 2147483648f,
                _ => 0,
            };
        }

        private static float[] Resample(float[] source, int from, int to)
        {
            int frames = source.Length / 2;
            int outFrames = (int)((long)frames * to / from);
            var result = new float[outFrames * 2];
            double step = (double)from / to;
            for (int i = 0; i < outFrames; i++)
            {
                double x = i * step;
                int a = (int)x;
                int b = Math.Min(a + 1, frames - 1);
                float t = (float)(x - a);
                result[i * 2] = source[a * 2] + (source[b * 2] - source[a * 2]) * t;
                result[i * 2 + 1] = source[a * 2 + 1] + (source[b * 2 + 1] - source[a * 2 + 1]) * t;
            }
            return result;
        }
    }

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

        public void PauseMusic(SoundData data, bool paused)
        {
            lock (_lock)
            {
                if (Find(data, true) is { } voice)
                    voice.Paused = paused;
            }
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

        public static bool IsRunning => _output != null;

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
                Console.WriteLine("Game audio could not start: " + ex.Message);
                _output = null;
            }
        }

        public static void Stop()
        {
            Mixer.StopAll();
        }
    }
}
