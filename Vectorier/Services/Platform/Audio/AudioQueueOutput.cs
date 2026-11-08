using System;
using System.Runtime.InteropServices;
using Vectorier.Gameplay.Audio;

namespace Vectorier.Services.Platform.Audio;

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
            mBytesPerPacket = 4,
            mFramesPerPacket = 1,
            mBytesPerFrame = 4,
            mChannelsPerFrame = 2,
            mBitsPerChannel = 16,
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
