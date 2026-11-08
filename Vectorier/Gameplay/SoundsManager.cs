#nullable disable warnings
using System;
using Vectorier.Gameplay.Rl;

namespace Vectorier.Gameplay;

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
