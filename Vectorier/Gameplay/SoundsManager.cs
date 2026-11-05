// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vectorier.Gameplay.Rl;

namespace Vectorier.Gameplay
{
    public class SoundsManager
    {
        public static Music CurrentMusic;

        public static void PlaySounds(SoundType sound)
        {
            var audio = ResourceManager.GetSound(sound.ToString());
            Raylib.PlaySound(audio);
        }

        public static void PlaySoundsOnce(string name, float vol)
        {
            var sound = ResourceManager.GetSound(name);
            Raylib.SetSoundVolume(sound, vol);
            Raylib.PlaySound(sound);
        }

        public static void PlaySoundsOnce(SoundType sound, float vol)
        {
            var audio = ResourceManager.GetSound(sound.ToString());
            Raylib.SetSoundVolume(audio, vol);
            Raylib.PlaySound(audio);
        }

        public static void PlaySounds(string name)
        {
            var sound = ResourceManager.GetSound(name);
            Raylib.PlaySound(sound);
        }

        /// <summary>Music muted (M): the music keeps playing silently, so unmuting picks it up where it is.</summary>
        public static bool MusicMuted;

        /// <summary>The level's track (the editor reads it when first played, then starts it again: see GameFiles).</summary>
        public static string CurrentMusicName;

        public static void PlayMusic(string name)
        {
            Console.WriteLine("PLAY MUSIC " + name);
            CurrentMusicName = name;
            var music = ResourceManager.GetMusic(name);
            CurrentMusic = music;
            Raylib.PlayMusicStream(music);
            ApplyMusicVolume();
        }

        /// <summary>The game was left: its music stops (the editor's addition: the game ended with its window).</summary>
        public static void StopMusic()
        {
            if (CurrentMusic.FrameCount > 0)
                Raylib.StopMusicStream(CurrentMusic);
            CurrentMusic = default;
            CurrentMusicName = null;
        }

        public static void ToggleMusicMute()
        {
            MusicMuted = !MusicMuted;
            ApplyMusicVolume();
        }

        static void ApplyMusicVolume()
        {
            if (CurrentMusic.FrameCount > 0)
                Raylib.SetMusicVolume(CurrentMusic, MusicMuted ? 0f : 1f);
        }
    }
}
