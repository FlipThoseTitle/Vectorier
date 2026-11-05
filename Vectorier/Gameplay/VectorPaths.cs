// Ported from Vector-Raylib-For-Vectorier (the game). In the editor the game's content is the active project, read
// beforehand into GameFiles: every path is relative to the project folder.
#nullable disable warnings
using System.IO;

namespace Vectorier.Gameplay
{
    /// <summary>Where the game reads its content: the project's layout (xmlroot, icons/tricks...).</summary>
    public static class VectorPaths
    {
        /// <summary>The project folder (the root of <see cref="GameFiles"/>).</summary>
        public static string Resources => string.Empty;

        public static string Textures => "textures";

        public static string AnimatedTextures => "animatedtextures";

        /// <summary>Trick icons: icons/tricks in an editor project, icons in the original Resources.</summary>
        public static string Icons => FirstExisting(Path.Combine("icons", "tricks"), "icons");

        public static string Sounds => "sounds";

        public static string Music => "music";

        public static string Animations => "animations";

        public static string AnimationBinary => Path.Combine(Animations, "data");

        /// <summary>Level and object XML: xmlroot in an editor project, xml in the original Resources.</summary>
        public static string Xml => FirstExisting("xmlroot", "xml");

        public static string XmlLevels => Path.Combine(Xml, "levels");

        public static string Models => "models";

        public static string Localization => "localization";

        /// <summary>The active project's name (set by the editor when a game starts).</summary>
        public static string ProjectName { get; set; }

        private static string FirstExisting(string preferred, string fallback) =>
            GameFiles.DirectoryExists(preferred) || !GameFiles.DirectoryExists(fallback) ? preferred : fallback;
    }
}
