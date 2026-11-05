using System;
using System.Runtime.InteropServices;
using System.Threading;
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

// ================= WINDOWS: waveOut ================= //

internal sealed class WaveOutOutput : IAudioOutput
{
    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormatEx
    {
        public ushort wFormatTag, nChannels;
        public uint nSamplesPerSec, nAvgBytesPerSec;
        public ushort nBlockAlign, wBitsPerSample, cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr lpData;
        public uint dwBufferLength, dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags, dwLoops;
        public IntPtr lpNext, reserved;
    }

    private const uint WaveMapper = 0xFFFFFFFF;
    private const uint WhdrDone = 0x00000001;
    private const int Buffers = 4;

    [DllImport("winmm.dll")] private static extern int waveOutOpen(out IntPtr hWaveOut, uint uDeviceId, ref WaveFormatEx lpFormat, IntPtr dwCallback, IntPtr dwInstance, uint dwFlags);
    [DllImport("winmm.dll")] private static extern int waveOutPrepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);
    [DllImport("winmm.dll")] private static extern int waveOutUnprepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);
    [DllImport("winmm.dll")] private static extern int waveOutWrite(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);
    [DllImport("winmm.dll")] private static extern int waveOutReset(IntPtr hWaveOut);
    [DllImport("winmm.dll")] private static extern int waveOutClose(IntPtr hWaveOut);

    private IntPtr _device;
    private Thread? _thread;
    private volatile bool _running;

    public void Start(Mixer mixer)
    {
        var format = new WaveFormatEx
        {
            wFormatTag = 1, nChannels = 2, nSamplesPerSec = Mixer.SampleRate, wBitsPerSample = 16, nBlockAlign = 4,
            nAvgBytesPerSec = Mixer.SampleRate * 4,
        };
        if (waveOutOpen(out _device, WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, 0) != 0)
            throw new InvalidOperationException("waveOutOpen failed");
        _running = true;
        _thread = new Thread(() => Run(mixer)) { IsBackground = true, Name = "Game audio", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    private void Run(Mixer mixer)
    {
        int bytes = DesktopAudio.BlockFrames * 4;
        int headerSize = Marshal.SizeOf<WaveHeader>();
        var headers = new IntPtr[Buffers];
        var data = new IntPtr[Buffers];
        var samples = new short[DesktopAudio.BlockFrames * 2];
        float[]? scratch = null;
        try
        {
            for (int i = 0; i < Buffers; i++)
            {
                data[i] = Marshal.AllocHGlobal(bytes);
                headers[i] = Marshal.AllocHGlobal(headerSize);
                Marshal.StructureToPtr(new WaveHeader { lpData = data[i], dwBufferLength = (uint)bytes, dwFlags = WhdrDone }, headers[i], false);
            }
            while (_running)
            {
                bool wrote = false;
                for (int i = 0; i < Buffers && _running; i++)
                {
                    var header = Marshal.PtrToStructure<WaveHeader>(headers[i]);
                    if ((header.dwFlags & WhdrDone) == 0)
                        continue;
                    waveOutUnprepareHeader(_device, headers[i], headerSize);
                    mixer.Render(samples, DesktopAudio.BlockFrames, ref scratch);
                    Marshal.Copy(samples, 0, data[i], samples.Length);
                    Marshal.StructureToPtr(new WaveHeader { lpData = data[i], dwBufferLength = (uint)bytes }, headers[i], false);
                    waveOutPrepareHeader(_device, headers[i], headerSize);
                    waveOutWrite(_device, headers[i], headerSize);
                    wrote = true;
                }
                if (!wrote)
                    Thread.Sleep(3);
            }
        }
        finally
        {
            waveOutReset(_device);
            for (int i = 0; i < Buffers; i++)
            {
                if (headers[i] != IntPtr.Zero)
                {
                    waveOutUnprepareHeader(_device, headers[i], headerSize);
                    Marshal.FreeHGlobal(headers[i]);
                }
                if (data[i] != IntPtr.Zero)
                    Marshal.FreeHGlobal(data[i]);
            }
            waveOutClose(_device);
        }
    }

    public void Dispose()
    {
        _running = false;
        _thread?.Join(500);
    }
}

// ================= LINUX: PulseAudio, else ALSA ================= //

internal static class LinuxOutput
{
    public static IAudioOutput? Create()
    {
        if (PulseOutput.IsAvailable())
            return new PulseOutput();
        if (AlsaOutput.IsAvailable())
            return new AlsaOutput();
        Console.WriteLine("Game audio: neither PulseAudio nor ALSA was found (the game is silent).");
        return null;
    }

    /// <summary>A thread writing blocks to a blocking output (PulseAudio's simple API, ALSA).</summary>
    public abstract class BlockingOutput : IAudioOutput
    {
        private Thread? _thread;
        private volatile bool _running;

        protected abstract bool Open();
        protected abstract bool Write(short[] samples, int frames);
        protected abstract void Close();

        public void Start(Mixer mixer)
        {
            if (!Open())
                throw new InvalidOperationException("The audio device could not be opened.");
            _running = true;
            _thread = new Thread(() =>
            {
                var samples = new short[DesktopAudio.BlockFrames * 2];
                float[]? scratch = null;
                try
                {
                    while (_running)
                    {
                        mixer.Render(samples, DesktopAudio.BlockFrames, ref scratch);
                        if (!Write(samples, DesktopAudio.BlockFrames))
                            Thread.Sleep(20);
                    }
                }
                finally
                {
                    Close();
                }
            }) { IsBackground = true, Name = "Game audio", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
        }

        public void Dispose()
        {
            _running = false;
            _thread?.Join(500);
        }
    }
}

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

// ================= macOS: Core Audio's AudioQueue ================= //

internal sealed class AudioQueueOutput : IAudioOutput
{
    private const string Lib = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    private const int Buffers = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioStreamBasicDescription
    {
        public double mSampleRate;
        public uint mFormatID, mFormatFlags, mBytesPerPacket, mFramesPerPacket, mBytesPerFrame, mChannelsPerFrame, mBitsPerChannel, mReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioQueueBuffer
    {
        public uint mAudioDataBytesCapacity;
        public IntPtr mAudioData;
        public uint mAudioDataByteSize;
        public IntPtr mUserData;
        public uint mPacketDescriptionCapacity;
        public IntPtr mPacketDescriptions;
        public uint mPacketDescriptionCount;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void OutputCallback(IntPtr userData, IntPtr queue, IntPtr buffer);

    [DllImport(Lib)] private static extern int AudioQueueNewOutput(ref AudioStreamBasicDescription format, OutputCallback callback, IntPtr userData, IntPtr runLoop, IntPtr runLoopMode, uint flags, out IntPtr queue);
    [DllImport(Lib)] private static extern int AudioQueueAllocateBuffer(IntPtr queue, uint size, out IntPtr buffer);
    [DllImport(Lib)] private static extern int AudioQueueEnqueueBuffer(IntPtr queue, IntPtr buffer, uint packets, IntPtr descriptions);
    [DllImport(Lib)] private static extern int AudioQueueStart(IntPtr queue, IntPtr startTime);
    [DllImport(Lib)] private static extern int AudioQueueStop(IntPtr queue, bool immediate);
    [DllImport(Lib)] private static extern int AudioQueueDispose(IntPtr queue, bool immediate);

    private IntPtr _queue;
    private Mixer? _mixer;
    private OutputCallback? _callback; // kept alive while the queue calls it
    private readonly short[] _samples = new short[DesktopAudio.BlockFrames * 2];
    private float[]? _scratch;

    public void Start(Mixer mixer)
    {
        _mixer = mixer;
        _callback = Fill;
        var format = new AudioStreamBasicDescription
        {
            mSampleRate = Mixer.SampleRate,
            mFormatID = 0x6C70636D, // 'lpcm'
            mFormatFlags = 0x4 | 0x8, // signed integer, packed
            mBytesPerPacket = 4, mFramesPerPacket = 1, mBytesPerFrame = 4, mChannelsPerFrame = 2, mBitsPerChannel = 16,
        };
        if (AudioQueueNewOutput(ref format, _callback, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out _queue) != 0)
            throw new InvalidOperationException("AudioQueueNewOutput failed");
        for (int i = 0; i < Buffers; i++)
        {
            if (AudioQueueAllocateBuffer(_queue, (uint)(DesktopAudio.BlockFrames * 4), out var buffer) == 0)
                Fill(IntPtr.Zero, _queue, buffer);
        }
        AudioQueueStart(_queue, IntPtr.Zero);
    }

    private void Fill(IntPtr userData, IntPtr queue, IntPtr bufferRef)
    {
        if (_mixer == null)
            return;
        var buffer = Marshal.PtrToStructure<AudioQueueBuffer>(bufferRef);
        _mixer.Render(_samples, DesktopAudio.BlockFrames, ref _scratch);
        Marshal.Copy(_samples, 0, buffer.mAudioData, _samples.Length);
        buffer.mAudioDataByteSize = (uint)(_samples.Length * 2);
        Marshal.StructureToPtr(buffer, bufferRef, false);
        AudioQueueEnqueueBuffer(queue, bufferRef, 0, IntPtr.Zero);
    }

    public void Dispose()
    {
        if (_queue == IntPtr.Zero)
            return;
        AudioQueueStop(_queue, true);
        AudioQueueDispose(_queue, true);
        _queue = IntPtr.Zero;
    }
}
