// The game reads moves.xml, the animation .bin files and the model XML straight from the disk. In the editor they
// are read beforehand through the editor's file system (which also works in the browser) and kept here, under the
// same relative paths the game uses ("animations/moves.xml", "animations/data/run.bin", "models/0.xml").
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.Text;

namespace Vectorier.Gameplay
{
    public static class GameplayFiles
    {
        private static readonly Dictionary<string, byte[]> Files = new(StringComparer.OrdinalIgnoreCase);

        public static string Key(string path) => (path ?? string.Empty).Replace('\\', '/').TrimStart('/');

        public static void Clear() => Files.Clear();

        public static void Set(string path, byte[] data) => Files[Key(path)] = data;

        public static bool Contains(string path) => Files.ContainsKey(Key(path));

        public static byte[] Get(string path) => Files.TryGetValue(Key(path), out var data) ? data : null;

        /// <summary>The file as text, like File.ReadAllText (UTF-8, a byte order mark is skipped).</summary>
        public static string GetText(string path)
        {
            var data = Get(path);
            if (data == null)
                return null;
            int start = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? 3 : 0;
            return Encoding.UTF8.GetString(data, start, data.Length - start);
        }
    }

    /// <summary>Where the game's content is, relative to the project folder (see <see cref="GameplayFiles"/>).</summary>
    public static class VectorPaths
    {
        public static string Animations => "animations";

        public static string AnimationBinary => "animations/data";

        public static string Models => "models";
    }

    /// <summary>The game's file lookups, answered from <see cref="GameplayFiles"/>.</summary>
    public static class ResourceManager
    {
        public static byte[] GetBinary(string path) => GameplayFiles.Get(path);
    }

    /// <summary>The game's file names are found whatever their casing (the store ignores case).</summary>
    public static class PathCase
    {
        public static string Resolve(string path) => path;
    }

    /// <summary>The editor's gameplay is silent: the moves' sounds are not played.</summary>
    public static class SoundsManager
    {
        public static void PlaySounds(SoundType type)
        {
        }

        public static void PlaySoundsOnce(string name, float volume = 1f)
        {
        }
    }
}
