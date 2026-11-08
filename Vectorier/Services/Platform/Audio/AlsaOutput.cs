using System;
using System.Runtime.InteropServices;
using Vectorier.Gameplay.Audio;

namespace Vectorier.Services.Platform.Audio;

internal sealed class AlsaOutput : LinuxOutput.BlockingOutput
{
    private const string Lib = "libasound.so.2";

    [DllImport(Lib)] private static extern int snd_pcm_open(out IntPtr pcm, string name, int stream, int mode);
    [DllImport(Lib)] private static extern int snd_pcm_set_params(IntPtr pcm, int format, int access, uint channels, uint rate, int softResample, uint latency);
    [DllImport(Lib)] private static extern IntPtr snd_pcm_writei(IntPtr pcm, short[] buffer, UIntPtr frames);
    [DllImport(Lib)] private static extern int snd_pcm_recover(IntPtr pcm, int err, int silent);
    [DllImport(Lib)] private static extern int snd_pcm_close(IntPtr pcm);

    private IntPtr _pcm;

    public static bool IsAvailable()
    {
        if (!NativeLibrary.TryLoad(Lib, out var handle))
            return false;
        NativeLibrary.Free(handle);
        return true;
    }

    protected override bool Open()
    {
        if (snd_pcm_open(out _pcm, "default", 0 /* playback */, 0) < 0)
            return false;
        // S16_LE, interleaved, 70 ms of latency.
        return snd_pcm_set_params(_pcm, 2, 3, 2, Mixer.SampleRate, 1, 70000) >= 0;
    }

    protected override bool Write(short[] samples, int frames)
    {
        long written = (long)snd_pcm_writei(_pcm, samples, (UIntPtr)frames);
        if (written < 0)
            return snd_pcm_recover(_pcm, (int)written, 1) >= 0;
        return true;
    }

    protected override void Close()
    {
        if (_pcm != IntPtr.Zero)
            snd_pcm_close(_pcm);
        _pcm = IntPtr.Zero;
    }
}
