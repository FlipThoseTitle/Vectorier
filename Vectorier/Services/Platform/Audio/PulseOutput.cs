using System;
using System.Runtime.InteropServices;
using Vectorier.Gameplay.Audio;

namespace Vectorier.Services.Platform.Audio;

internal sealed class PulseOutput : LinuxOutput.BlockingOutput
{
    private const string Lib = "libpulse-simple.so.0";

    [StructLayout(LayoutKind.Sequential)]
    private struct SampleSpec
    {
        public int format;
        public uint rate;
        public byte channels;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BufferAttr
    {
        public uint maxlength, tlength, prebuf, minreq, fragsize;
    }

    [DllImport(Lib)] private static extern IntPtr pa_simple_new(string? server, string name, int dir, string? dev, string streamName, ref SampleSpec ss, IntPtr map, ref BufferAttr attr, out int error);
    [DllImport(Lib)] private static extern int pa_simple_write(IntPtr s, short[] data, UIntPtr bytes, out int error);
    [DllImport(Lib)] private static extern void pa_simple_free(IntPtr s);

    private IntPtr _stream;

    public static bool IsAvailable() => NativeLibrary.TryLoad(Lib, out var handle) && Free(handle);

    private static bool Free(IntPtr handle)
    {
        NativeLibrary.Free(handle);
        return true;
    }

    protected override bool Open()
    {
        var spec = new SampleSpec { format = 3 /* PA_SAMPLE_S16LE */, rate = Mixer.SampleRate, channels = 2 };
        // About 70 ms of buffering: short enough for game sounds.
        var attr = new BufferAttr { maxlength = uint.MaxValue, tlength = (uint)(DesktopAudio.BlockFrames * 4 * 3), prebuf = uint.MaxValue, minreq = uint.MaxValue, fragsize = uint.MaxValue };
        _stream = pa_simple_new(null, "Vectorier", 1 /* PA_STREAM_PLAYBACK */, null, "Game", ref spec, IntPtr.Zero, ref attr, out _);
        return _stream != IntPtr.Zero;
    }

    protected override bool Write(short[] samples, int frames) => pa_simple_write(_stream, samples, (UIntPtr)(frames * 4), out _) >= 0;

    protected override void Close()
    {
        if (_stream != IntPtr.Zero)
            pa_simple_free(_stream);
        _stream = IntPtr.Zero;
    }
}
