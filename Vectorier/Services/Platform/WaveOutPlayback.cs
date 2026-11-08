using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Vectorier.Services.Platform;

/// <summary>One WAV buffer played through the Windows waveOut API (any format the ACM can convert).</summary>
internal sealed class WaveOutPlayback
{
    private const uint WaveMapper = 0xFFFFFFFF;
    private const int WhdrDone = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags;
        public uint dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    [DllImport("winmm.dll")] private static extern int waveOutOpen(out IntPtr hWaveOut, uint uDeviceId, IntPtr lpFormat, IntPtr dwCallback, IntPtr dwInstance, uint dwFlags);
    [DllImport("winmm.dll")] private static extern int waveOutPrepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);
    [DllImport("winmm.dll")] private static extern int waveOutUnprepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);
    [DllImport("winmm.dll")] private static extern int waveOutWrite(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);
    [DllImport("winmm.dll")] private static extern int waveOutReset(IntPtr hWaveOut);
    [DllImport("winmm.dll")] private static extern int waveOutClose(IntPtr hWaveOut);
    [DllImport("winmm.dll")] private static extern int waveOutSetVolume(IntPtr hWaveOut, uint dwVolume);

    private IntPtr _handle;
    private IntPtr _format;
    private IntPtr _data;
    private IntPtr _header;
    private int _closed;
    private readonly Action _finished;

    private WaveOutPlayback(Action finished) => _finished = finished;

    /// <summary>Starts playing a WAV file's bytes, or returns null when the data can't be played this way.</summary>
    public static WaveOutPlayback? Start(byte[] wav, double volume, Action finished)
    {
        if (!TryParse(wav, out int fmtOffset, out int fmtLength, out int dataOffset, out int dataLength))
            return null;

        var p = new WaveOutPlayback(finished);
        try
        {
            // WAVEFORMATEX needs its cbSize field: a plain PCM "fmt " chunk is 16 bytes, so pad it to 18.
            int formatSize = Math.Max(18, fmtLength);
            p._format = Marshal.AllocHGlobal(formatSize);
            var format = new byte[formatSize];
            Array.Copy(wav, fmtOffset, format, 0, fmtLength);
            Marshal.Copy(format, 0, p._format, formatSize);

            if (waveOutOpen(out p._handle, WaveMapper, p._format, IntPtr.Zero, IntPtr.Zero, 0) != 0)
            {
                p.Free();
                return null;
            }

            p._data = Marshal.AllocHGlobal(dataLength);
            Marshal.Copy(wav, dataOffset, p._data, dataLength);
            var header = new WaveHeader { lpData = p._data, dwBufferLength = (uint)dataLength };
            p._header = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());
            Marshal.StructureToPtr(header, p._header, false);

            p.SetVolume(volume);
            if (waveOutPrepareHeader(p._handle, p._header, Marshal.SizeOf<WaveHeader>()) != 0 ||
                waveOutWrite(p._handle, p._header, Marshal.SizeOf<WaveHeader>()) != 0)
            {
                p.Close();
                return null;
            }
            _ = p.WatchAsync();
            return p;
        }
        catch
        {
            p.Close();
            return null;
        }
    }

    public void SetVolume(double volume)
    {
        if (_handle == IntPtr.Zero || _closed != 0) return;
        uint v = (uint)(Math.Clamp(volume, 0, 1) * 0xFFFF);
        waveOutSetVolume(_handle, v | (v << 16));
    }

    public void Stop() => Close();

    /// <summary>Polls the header's "done" flag; the device has no window to send messages to.</summary>
    private async Task WatchAsync()
    {
        while (Volatile.Read(ref _closed) == 0)
        {
            await Task.Delay(40).ConfigureAwait(false);
            if (Volatile.Read(ref _closed) != 0) break;
            var header = Marshal.PtrToStructure<WaveHeader>(_header);
            if ((header.dwFlags & WhdrDone) != 0)
            {
                Close();
                break;
            }
        }
    }

    private void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        try
        {
            if (_handle != IntPtr.Zero)
            {
                waveOutReset(_handle);
                if (_header != IntPtr.Zero)
                    waveOutUnprepareHeader(_handle, _header, Marshal.SizeOf<WaveHeader>());
                waveOutClose(_handle);
            }
        }
        catch
        {
            // ignored
        }
        Free();
        _finished();
    }

    private void Free()
    {
        if (_header != IntPtr.Zero) { Marshal.FreeHGlobal(_header); _header = IntPtr.Zero; }
        if (_data != IntPtr.Zero) { Marshal.FreeHGlobal(_data); _data = IntPtr.Zero; }
        if (_format != IntPtr.Zero) { Marshal.FreeHGlobal(_format); _format = IntPtr.Zero; }
        _handle = IntPtr.Zero;
    }

    /// <summary>Finds the "fmt " and "data" chunks of a RIFF/WAVE file.</summary>
    private static bool TryParse(byte[] d, out int fmtOffset, out int fmtLength, out int dataOffset, out int dataLength)
    {
        fmtOffset = fmtLength = dataOffset = dataLength = 0;
        if (d.Length < 12 || d[0] != 'R' || d[1] != 'I' || d[2] != 'F' || d[3] != 'F' || d[8] != 'W' || d[9] != 'A' || d[10] != 'V' || d[11] != 'E')
            return false;
        int pos = 12;
        while (pos + 8 <= d.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(d, pos, 4);
            int size = BitConverter.ToInt32(d, pos + 4);
            int body = pos + 8;
            if (size < 0) return false;
            if (id == "fmt ")
            {
                fmtOffset = body;
                fmtLength = Math.Min(size, d.Length - body);
            }
            else if (id == "data")
            {
                dataOffset = body;
                dataLength = Math.Min(size, d.Length - body);
            }
            pos = body + size + (size & 1);
        }
        return fmtLength >= 16 && dataLength > 0;
    }
}
