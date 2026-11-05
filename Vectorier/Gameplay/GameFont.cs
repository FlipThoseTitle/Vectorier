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
using System.Numerics;
using System.Reflection;

namespace Vectorier.Gameplay
{
    /// <summary>
    /// The game's font: Play, Vector's own font (Unity's Resources/fonts), built into the executable. Play Regular
    /// is used for the debug UI, Play Bold for the game's own texts (trigger messages, tutorial hints), as in Unity.
    /// Latin, Latin-1 and Cyrillic characters are loaded (Play covers both alphabets).
    /// </summary>
    public static class GameFont
    {
        private const int BaseSize = 64;

        private static Font? _regular;
        private static Font? _bold;

        /// <summary>Play Regular.</summary>
        public static Font Font => _regular ??= Load("Play-Regular.ttf");

        /// <summary>Play Bold.</summary>
        public static Font Bold => _bold ??= Load("Play-Bold.ttf");

        private static int[] Codepoints()
        {
            var list = new List<int>();
            for (int c = 32; c < 127; c++) list.Add(c);         // ASCII
            for (int c = 160; c < 384; c++) list.Add(c);        // Latin-1, Latin Extended-A
            for (int c = 0x400; c < 0x460; c++) list.Add(c);    // Cyrillic
            list.AddRange(new[] { 0x2013, 0x2014, 0x2018, 0x2019, 0x201C, 0x201D, 0x2026, 0x2022, 0x2116 });
            return list.ToArray();
        }

        // Play is one of the editor's assets (Assets/Game/Fonts): Avalonia draws it at any size, no glyph atlas.
        private static Font Load(string name)
        {
            var family = new Avalonia.Media.FontFamily("avares://Vectorier.Core/Assets/Game/Fonts#Play");
            var weight = name.Contains("Bold") ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal;
            return Raylib.LoadFont(new Avalonia.Media.Typeface(family, Avalonia.Media.FontStyle.Normal, weight), BaseSize);
        }

        // ================= THE EDITOR'S UI FONT ================= //

        // The debug UI (buttons, FPS, the move's name, overlays, labels) is the editor's: Arial, or the closest font
        // the system has (Liberation Sans / Arimo on Linux, Helvetica on macOS), the editor's Inter otherwise (the
        // browser). The game's own texts (messages, tutorial hints) stay in Play.
        private const string UiFamily = "Arial, Liberation Sans, Arimo, Helvetica, fonts:Inter#Inter";

        private static Font? _ui;
        private static Font? _uiBold;

        public static Font Ui => _ui ??= Raylib.LoadFont(new Avalonia.Media.Typeface(
            new Avalonia.Media.FontFamily(UiFamily), Avalonia.Media.FontStyle.Normal, Avalonia.Media.FontWeight.Normal), BaseSize);

        public static Font UiBold => _uiBold ??= Raylib.LoadFont(new Avalonia.Media.Typeface(
            new Avalonia.Media.FontFamily(UiFamily), Avalonia.Media.FontStyle.Normal, Avalonia.Media.FontWeight.Bold), BaseSize);

        public static void DrawUi(string text, float x, float y, float size, Color color) =>
            Raylib.DrawTextEx(Ui, text, new Vector2(x, y), size, 0, color);

        public static void DrawUiBold(string text, float x, float y, float size, Color color) =>
            Raylib.DrawTextEx(UiBold, text, new Vector2(x, y), size, 0, color);

        public static float UiWidth(string text, float size) => Raylib.MeasureTextEx(Ui, text, size, 0).X;

        public static float UiBoldWidth(string text, float size) => Raylib.MeasureTextEx(UiBold, text, size, 0).X;

        // ================= PLAY ================= //

        public static void Draw(string text, float x, float y, float size, Color color) =>
            Raylib.DrawTextEx(Font, text, new Vector2(x, y), size, size / 10f, color);

        public static void DrawBold(string text, float x, float y, float size, Color color) =>
            Raylib.DrawTextEx(Bold, text, new Vector2(x, y), size, 0, color);
    }
}
