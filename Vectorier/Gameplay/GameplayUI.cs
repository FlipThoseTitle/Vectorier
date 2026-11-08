#nullable disable warnings
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Vectorier.Gameplay.Controllers;
using Vectorier.Gameplay.Rl;

namespace Vectorier.Gameplay;

/// <summary>
/// The game's own on-screen UI, ported from Vector's Unity version (MessageOnScreen, TutorialUIController /
/// TutorialKeyboard): the text of a trigger's MessageOnScreen action, and the tutorial hint of a Help area (the
/// key to press, pulsing, with its description) shown while the game is slowed down and waiting for that key.
/// Drawn over the level, under the debug UI; it stays visible when the debug UI is hidden (H).
/// </summary>
public static class GameplayUI
{
    // ================= MESSAGE ON SCREEN ================= //

    const int FadeTicks = 30; // Unity: DOFade over 0.5 s

    static string? _message;
    static Color _messageColor;
    static int _messageHold;
    static int _messageAge;

    /// <summary>
    /// A trigger's &lt;MessageOnScreen&gt;: the text (a localization key, or the text itself when it is not one)
    /// fades in over 0.5 s, stays Frames / 60 whole seconds (as in Unity, where the division is an integer one),
    /// and fades out over 0.5 s. A new message replaces the one on screen.
    /// </summary>
    public static void ShowMessage(string text, int frames, Color color)
    {
        _message = Localization.TranslationOrKey(text);
        _messageColor = color;
        _messageHold = frames / 60 * 60;
        _messageAge = 0;
    }

    /// <summary>One game update (the message's time stops while the game is paused).</summary>
    public static void Tick()
    {
        if (_message == null)
            return;
        _messageAge++;
        if (_messageAge > FadeTicks * 2 + _messageHold)
            _message = null;
    }

    /// <summary>Restart: the tutorial hint goes; a message keeps fading out over the new run, as in Unity.</summary>
    public static void Reset()
    {
        _tutorialKey = null;
    }

    static void DrawMessage()
    {
        if (_message == null)
            return;
        float alpha = _messageAge < FadeTicks ? _messageAge / (float)FadeTicks
            : _messageAge > FadeTicks + _messageHold ? 1 - (_messageAge - FadeTicks - _messageHold) / (float)FadeTicks
            : 1;
        alpha = Math.Clamp(alpha, 0, 1) * (_messageColor.A / 255f);
        // Unity (Main.unity): Play Bold 75 on a 2732x1536 canvas matched to the height, white unless the
        // action says otherwise, no outline or shadow, aligned bottom-center in a box whose bottom is 310 units
        // above the screen's center.
        float h = Raylib.GetScreenHeight();
        float size = MathF.Round(75 * h / ReferenceHeight);
        var lines = Wrap(_message, size, Raylib.GetScreenWidth());
        float bottom = h / 2 - 310 * h / ReferenceHeight;
        float y = bottom - lines.Count * size;
        foreach (var line in lines)
        {
            CenteredText(line, y, size, _messageColor, alpha);
            y += size;
        }
    }

    // ================= TUTORIAL ================= //

    static KeyboardKey? _tutorialKey;
    static string _tutorialText = string.Empty;
    static double _tutorialStart;

    static Texture2D _button, _arrow;
    static bool _texturesLoaded;
    static int _texturesGeneration;

    /// <summary>A Help area asks for its key (LevelMainController.TutorialAreaActivate).</summary>
    public static void ShowTutorial(KeyVariables key, string description)
    {
        _tutorialKey = key.Key;
        _tutorialText = Localization.Translation(description);
        _tutorialStart = Raylib.GetTime();
    }

    /// <summary>The right key was pressed, or the level restarted (LevelMainController.TutorialUnLockGame).</summary>
    public static void HideTutorial() => _tutorialKey = null;

    static void LoadTextures()
    {
        // Leaving or reloading a level drops every texture handle (Raylib.ResetTextures): the old ones would
        // draw nothing (or another picture), so the hint's pictures are registered again then.
        if (_texturesLoaded && _texturesGeneration == Raylib.TextureGeneration)
            return;
        _texturesLoaded = true;
        _texturesGeneration = Raylib.TextureGeneration;
        _button = LoadEmbedded("help_btn.png");
        _arrow = LoadEmbedded("help_arrow_up.png");
    }

    static Texture2D LoadEmbedded(string name)
    {
        // The tutorial hint pictures are editor assets (Assets/Game).
        var uri = new Uri("avares://Vectorier.Core/Assets/Game/" + name);
        if (!Avalonia.Platform.AssetLoader.Exists(uri))
            return default;
        using var stream = Avalonia.Platform.AssetLoader.Open(uri);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var image = Raylib.LoadImageFromMemory(".png", memory.ToArray());
        var texture = Raylib.LoadTextureFromImage(image);
        return texture;
    }

    /// <summary>
    /// TutorialKeyboard: the key button with an arrow toward the key's direction, pulsing (wait 0.2 s, shrink to
    /// 85% in 0.3 s, hold 0.3 s, grow back in 0.6 s, again), and the description under it.
    /// </summary>
    static void DrawTutorial()
    {
        if (_tutorialKey is not KeyboardKey key)
            return;
        LoadTextures();

        float unit = Raylib.GetScreenHeight() / 720f;
        var center = new Vector2(Raylib.GetScreenWidth() / 2f, Raylib.GetScreenHeight() * 0.62f);

        double t = (Raylib.GetTime() - _tutorialStart) % 1.4;
        float scale = t < 0.2 ? 1
            : t < 0.5 ? Lerp(1, 0.85f, (float)((t - 0.2) / 0.3))
            : t < 0.8 ? 0.85f
            : Lerp(0.85f, 1, (float)((t - 0.8) / 0.6));
        float size = 96 * unit * 1.25f * scale;

        if (_button.Id != 0)
        {
            Raylib.DrawTexturePro(_button, new Vectorier.Gameplay.Rl.Rectangle(0, 0, _button.Width, _button.Height),
                new Vectorier.Gameplay.Rl.Rectangle(center.X, center.Y, size, size * _button.Height / _button.Width),
                new Vector2(size / 2, size * _button.Height / _button.Width / 2), 0, new Color(255, 255, 255, 235));
        }
        else
        {
            Raylib.DrawRectangleRounded(new Vectorier.Gameplay.Rl.Rectangle(center.X - size / 2, center.Y - size / 2, size, size), 0.2f, 6, new Color(240, 240, 240, 235));
        }

        float rotation = key switch
        {
            KeyboardKey.Down => 180,
            KeyboardKey.Left => -90,
            KeyboardKey.Right => 90,
            _ => 0,
        };
        float arrow = size * 0.62f;
        if (_arrow.Id != 0)
        {
            Raylib.DrawTexturePro(_arrow, new Vectorier.Gameplay.Rl.Rectangle(0, 0, _arrow.Width, _arrow.Height),
                new Vectorier.Gameplay.Rl.Rectangle(center.X, center.Y, arrow, arrow), new Vector2(arrow / 2, arrow / 2), rotation,
                new Color(110, 110, 110, 255));
        }

        // Unity: Play Bold 60 on the 1536-high canvas, white, no outline or shadow.
        float textSize = MathF.Round(60 * Raylib.GetScreenHeight() / ReferenceHeight);
        float y = center.Y + 96 * unit * 1.25f / 2 + 14 * unit;
        foreach (var line in Wrap(_tutorialText, textSize, Raylib.GetScreenWidth() * 0.8f))
        {
            CenteredText(line, y, textSize, Color.White, 1);
            y += textSize;
        }
    }

    // ================= DRAWING ================= //

    public static void Draw()
    {
        DrawTutorial();
        DrawMessage();
    }

    static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0, 1);

    /// <summary>Height of Unity's UI canvas (CanvasScaler reference 2732x1536, matched to the height).</summary>
    const float ReferenceHeight = 1536;

    static float Width(string text, float size) => Raylib.MeasureTextEx(GameFont.Bold, text, size, 0).X;

    /// <summary>Plain Play Bold text, as Unity's UI Text: no outline, shadow or background.</summary>
    static void CenteredText(string text, float y, float size, Color color, float alpha)
    {
        float x = MathF.Round((Raylib.GetScreenWidth() - Width(text, size)) / 2);
        GameFont.DrawBold(text, x, MathF.Round(y), size, new Color(color.R, color.G, color.B, (byte)(255 * alpha)));
    }

    static List<string> Wrap(string text, float size, float maxWidth)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\\n", "\n").Split('\n'))
        {
            string current = string.Empty;
            foreach (var word in paragraph.Split(' '))
            {
                string candidate = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && Width(candidate, size) > maxWidth)
                {
                    lines.Add(current);
                    current = word;
                }
                else
                {
                    current = candidate;
                }
            }
            lines.Add(current);
        }
        return lines;
    }
}
