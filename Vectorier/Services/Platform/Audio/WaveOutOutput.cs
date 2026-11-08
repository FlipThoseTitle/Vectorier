using System;
using System.Runtime.InteropServices;
using System.Threading;
using Vectorier.Gameplay.Audio;

namespace Vectorier.Services.Platform.Audio;

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
            wFormatTag = 1,
            nChannels = 2,
            nSamplesPerSec = Mixer.SampleRate,
            wBitsPerSample = 16,
            nBlockAlign = 4,
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
