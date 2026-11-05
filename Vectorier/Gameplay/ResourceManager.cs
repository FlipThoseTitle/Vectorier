// Ported from Vector-Raylib-For-Vectorier (the game); in the editor its files come from GameFiles and its textures are
// the editor's bitmaps (TextureLibrary), sounds and music are decoded for the game mixer.
#nullable disable warnings
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vectorier.Gameplay.Audio;
using Vectorier.Gameplay.Rl;

namespace Vectorier.Gameplay
{
    public static class ResourceManager
    {
        public static Dictionary<string, string> images = new(StringComparer.OrdinalIgnoreCase);

        public static Dictionary<string, Texture2D> textureCache = new(StringComparer.OrdinalIgnoreCase);

        public static Dictionary<string, string> sounds = new(StringComparer.OrdinalIgnoreCase);

        public static Dictionary<string, Sound> soundCache = new(StringComparer.OrdinalIgnoreCase);

        public static Dictionary<string, string> music = new(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, Music> musicCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Files of a folder; nothing when the folder does not exist (a project may lack music, icons...).</summary>
        private static string[] FilesIn(string folder) => GameFiles.DirectoryExists(folder) ? GameFiles.GetFiles(folder) : Array.Empty<string>();

        public static void LoadResources()
        {
            images.Clear();
            sounds.Clear();
            music.Clear();
            foreach (var imageFile in FilesIn(VectorPaths.Textures))
            {
                if (!imageFile.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                images.TryAdd(Path.GetFileNameWithoutExtension(imageFile), imageFile);
            }

            foreach (var icon in FilesIn(VectorPaths.Icons))
            {
                images.TryAdd(Path.GetFileNameWithoutExtension(icon), icon);
            }

            foreach (var soundFile in FilesIn(VectorPaths.Sounds))
            {
                sounds.TryAdd(Path.GetFileNameWithoutExtension(soundFile), soundFile);
            }

            foreach (var musicFile in FilesIn(VectorPaths.Music))
            {
                music.TryAdd(Path.GetFileNameWithoutExtension(musicFile), musicFile);
            }
        }

        // The editor's textures are decoded once, for the Scene View: nothing to prefetch.
        public static void PrefetchImages(IEnumerable<string> names) { }

        public static void ReleasePrefetched() { }

        public static byte[] GetBinary(string path) => GameFiles.ReadAllBytes(PathCase.Resolve(path));

        public static Texture2D GetTexture(string name)
        {
            if (name == null || !images.ContainsKey(name))
            {
                return default(Texture2D);
            }
            if (textureCache.TryGetValue(name, out var cached))
            {
                return cached;
            }

            // A project texture: the editor's bitmap. An icon (or a texture the editor has not indexed): its PNG.
            Texture2D texture = default;
            if (GameFiles.TextureProvider?.Invoke(name) is { } image)
            {
                texture = Raylib.RegisterTexture(image);
            }
            else if (GameFiles.ReadAllBytes(images[name]) is { } data)
            {
                texture = Raylib.LoadTextureFromImage(Raylib.LoadImageFromMemory(".png", data));
            }
            else
            {
                GameFiles.TextureMissing?.Invoke(name);
                return default(Texture2D);
            }
            textureCache[name] = texture;
            return texture;
        }

        public static Sound GetSound(string name)
        {
            if (name == null || !sounds.ContainsKey(name))
            {
                return default(Sound);
            }
            if (soundCache.TryGetValue(name, out var cached))
            {
                return cached;
            }
            var data = GameFiles.ReadAllBytes(sounds[name]);
            var sound = data == null ? default : Raylib.LoadSoundFromData(SoundData.FromWav(name, data));
            soundCache[name] = sound;
            return sound;
        }

        public static Music GetMusic(string name)
        {
            if (name == null || !music.ContainsKey(name))
            {
                return default;
            }
            if (musicCache.TryGetValue(name, out var cached))
            {
                return cached;
            }
            var data = GameFiles.ReadAllBytes(music[name]);
            if (data == null)
            {
                // Music is large: it is read when first played (it starts once it is there).
                GameFiles.MusicMissing?.Invoke(name);
                return default;
            }
            var track = Raylib.LoadMusicFromData(SoundData.FromWav(name, data));
            musicCache[name] = track;
            return track;
        }

        public static void ClearTextureCache()
        {
            textureCache.Clear();
            Raylib.ResetTextures();
        }

        public static void ClearSoundCache()
        {
            soundCache.Clear();
        }

        /// <summary>Sounds and music decoded once for the mixer stay while the editor runs (a replay is instant).</summary>
        public static void ClearMusicCache() => musicCache.Clear();

        public static bool FileExists(string p_path, out string existingPath, params string[] exts)
        {
            existingPath = p_path;

            foreach (var ext in exts)
            {
                existingPath = PathCase.Resolve(p_path + ext);

                if (GameFiles.FileExists(existingPath))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
