#nullable enable
using System;

namespace Vectorier.Gameplay.Audio;

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
