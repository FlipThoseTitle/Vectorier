using System;
using System.Threading;
using Vectorier.Gameplay.Audio;

namespace Vectorier.Services.Platform.Audio;

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
            })
            { IsBackground = true, Name = "Game audio", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
        }

        public void Dispose()
        {
            _running = false;
            _thread?.Join(500);
        }
    }
}
