#nullable disable warnings
using System.Numerics;
using Vectorier.Gameplay.Rl;

namespace Vectorier.Gameplay;

/// <summary>
/// The game's font: Play, Vector's own font (Unity's Resources/fonts), built into the executable. Play Regular
/// is used for the debug UI, Play Bold for the game's own texts (trigger messages, tutorial hints), as in Unity.
/// Latin, Latin-1 and Cyrillic characters are loaded (Play covers both alphabets).
/// </summary>
public static class GameFont
{
    private const int BaseSize = 64;
    private static Font? _bold;

    /// <summary>Play Bold.</summary>
    public static Font Bold => _bold ??= Load("Play-Bold.ttf");

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

    public static void DrawBold(string text, float x, float y, float size, Color color) =>
        Raylib.DrawTextEx(Bold, text, new Vector2(x, y), size, 0, color);
}
