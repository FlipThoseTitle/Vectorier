using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Vectorier.Services.Platform;

/// <summary>
/// Sound previews for the desktop. Windows plays WAV files through waveOut (adjustable volume, end detection);
/// other formats and other systems use the platform's command-line player (afplay, paplay, ffplay), whose exit
/// marks the end of playback.
/// </summary>
public sealed class SoundPreview
{
    private readonly object _lock = new();
    private WaveOutPlayback? _wave;
    private Process? _process;
    private TaskCompletionSource? _done;
    private double _volume = 1;

    public Task PlayAsync(string file, double volume)
    {
        Stop();
        _volume = Math.Clamp(volume, 0, 1);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock) _done = done;

        try
        {
            if (OperatingSystem.IsWindows() && file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                var wave = WaveOutPlayback.Start(File.ReadAllBytes(file), _volume, () => Finish(done));
                if (wave != null)
                {
                    lock (_lock) _wave = wave;
                    return done.Task;
                }
            }

            var process = StartProcess(file, _volume);
            if (process == null)
            {
                done.TrySetResult();
                return done.Task;
            }
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => Finish(done);
            lock (_lock) _process = process;
            if (process.HasExited) Finish(done);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Sound preview failed: " + ex.Message);
            done.TrySetResult();
        }
        return done.Task;
    }

    private void Finish(TaskCompletionSource done)
    {
        lock (_lock)
        {
            if (ReferenceEquals(_done, done))
            {
                _done = null;
                _wave = null;
                _process = null;
            }
        }
        done.TrySetResult();
    }

    public void SetVolume(double volume)
    {
        _volume = Math.Clamp(volume, 0, 1);
        lock (_lock) _wave?.SetVolume(_volume);
    }

    public void Stop()
    {
        WaveOutPlayback? wave;
        Process? process;
        TaskCompletionSource? done;
        lock (_lock)
        {
            wave = _wave;
            process = _process;
            done = _done;
            _wave = null;
            _process = null;
            _done = null;
        }
        wave?.Stop();
        try
        {
            if (process is { HasExited: false })
                process.Kill();
        }
        catch
        {
            // already gone
        }
        done?.TrySetResult();
    }

    private static Process? StartProcess(string file, double volume)
    {
        if (OperatingSystem.IsMacOS())
            return TryStart("afplay", "-v", volume.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), file);
        if (OperatingSystem.IsWindows())
            return null; // non-WAV files: no built-in player without a window
        return TryStart("paplay", $"--volume={(int)(volume * 65536)}", file)
               ?? TryStart("ffplay", "-nodisp", "-autoexit", "-loglevel", "quiet", "-volume", ((int)(volume * 100)).ToString(), file)
               ?? TryStart("aplay", "-q", file);
    }

    private static Process? TryStart(string exe, params string[] args)
    {
        try
        {
            var info = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
            foreach (var arg in args)
                info.ArgumentList.Add(arg);
            return Process.Start(info);
        }
        catch
        {
            return null;
        }
    }
}

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
