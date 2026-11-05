using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading;
using Vectorier.Gameplay.Audio;

namespace Vectorier.Browser;

/// <summary>
/// The game's sound in the browser: the mixer's frames are rendered in blocks a little ahead of time and queued in
/// a Web Audio context (vectorier.js), on the page's own thread (a timer keeps about a tenth of a second queued).
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed partial class WebAudioOutput : IAudioOutput
{
    private const int BlockFrames = 1024;
    private const double Ahead = 0.12; // seconds of sound queued in front of the one playing

    private Mixer? _mixer;
    private Timer? _timer;
    private readonly float[] _block = new float[BlockFrames * 2];
    private readonly byte[] _bytes = new byte[BlockFrames * 2 * sizeof(float)];

    public void Start(Mixer mixer)
    {
        if (!StartJs(Mixer.SampleRate))
            throw new InvalidOperationException("Web Audio is not available.");
        _mixer = mixer;
        _timer = new Timer(_ => Pump(), null, 0, 20);
    }

    private void Pump()
    {
        if (_mixer == null)
            return;
        try
        {
            // Nothing queued while the page has not been allowed to play sound yet (no click so far).
            int guard = 8;
            while (guard-- > 0 && QueuedJs() < Ahead)
            {
                _mixer.Render(_block, BlockFrames);
                MemoryMarshal.AsBytes(_block.AsSpan()).CopyTo(_bytes);
                if (!PushJs(_bytes, BlockFrames))
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Game audio: " + ex.Message);
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        _mixer = null;
        try { StopJs(); } catch { }
    }

    [JSImport("audioStart", "vectorier")]
    private static partial bool StartJs(int sampleRate);

    [JSImport("audioQueued", "vectorier")]
    private static partial double QueuedJs();

    [JSImport("audioPush", "vectorier")]
    private static partial bool PushJs([JSMarshalAs<JSType.MemoryView>] Span<byte> samples, int frames);

    [JSImport("audioStop", "vectorier")]
    private static partial void StopJs();
}
