#nullable disable warnings
using System;
using System.Numerics;
using Vectorier.Gameplay.Rl;

namespace Vectorier.Gameplay;

/// <summary>
/// A small debug panel button: "[Key] Name". Dark and translucent so it stays out of the way; a lit marker shows
/// whether a toggle is on, and a disabled button is dimmed. <see cref="Scale"/> sizes it (the debug panel shrinks
/// to fit a Game View that is short, e.g. with Windows display scaling above 100%).
/// </summary>
class Button
{
    /// <summary>The height at scale 1.</summary>
    public const float BaseHeight = 24;
    const float BaseFontSize = 15;
    const float BasePad = 8;

    /// <summary>Size factor of the button (its rectangle, text and marker).</summary>
    public readonly float Scale;

    float Height => BaseHeight * Scale;
    float FontSize => BaseFontSize * Scale;
    float Pad => BasePad * Scale;

    public Vectorier.Gameplay.Rl.Rectangle Rectangle;

    readonly string Key;
    readonly string Text;

    /// <summary>Optional live text (e.g. "Anti-alias: High"); replaces Text when set.</summary>
    public Func<string>? DynamicText;

    public Action OnClick;

    /// <summary>Toggle state (null: a plain action button).</summary>
    public Func<bool>? IsOn;

    /// <summary>When it returns false the button is dimmed and does nothing.</summary>
    public Func<bool>? IsEnabled;

    bool IsHovered;
    bool IsPressed;

    static readonly Color Background = new(20, 20, 24, 150);
    static readonly Color BackgroundHover = new(45, 45, 55, 190);
    static readonly Color BackgroundPressed = new(70, 70, 85, 210);
    static readonly Color KeyColor = new(255, 200, 80, 255);
    static readonly Color OnColor = new(90, 200, 120, 255);
    static readonly Color OffColor = new(110, 110, 120, 255);

    public Button(float x, float y, float width, string key, string text, Action onClick, float scale = 1)
    {
        Scale = scale;
        Rectangle = new Vectorier.Gameplay.Rl.Rectangle(x, y, width, BaseHeight * scale);
        Key = key;
        Text = text;
        OnClick = onClick;
    }

    bool Enabled => IsEnabled?.Invoke() ?? true;

    public void Update()
    {
        if (Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), Rectangle))
        {
            IsHovered = true;
            if (Raylib.IsMouseButtonDown(MouseButton.Left))
            {
                IsPressed = true;
            }
            else
            {
                if (IsPressed && Enabled)
                    OnClick?.Invoke();
                IsPressed = false;
            }
        }
        else
        {
            IsHovered = false;
            IsPressed = false;
        }
    }

    public void Draw()
    {
        bool enabled = Enabled;
        var bg = !enabled ? Background : IsPressed ? BackgroundPressed : IsHovered ? BackgroundHover : Background;
        Raylib.DrawRectangleRounded(Rectangle, 0.25f, 4, bg);

        float x = Rectangle.X + Pad;
        float textY = Rectangle.Y + (Height - FontSize) / 2;
        byte alpha = (byte)(enabled ? 255 : 110);

        // Toggle marker: a small lit / unlit dot.
        if (IsOn != null)
        {
            bool on = IsOn();
            var dot = on ? OnColor : OffColor;
            Raylib.DrawCircleV(new Vector2(x + 3 * Scale, Rectangle.Y + Height / 2), 3.5f * Scale, new Color(dot.R, dot.G, dot.B, alpha));
        }
        x += 13 * Scale; // action buttons keep the same text column as the toggles

        string key = Key;
        if (key.Length > 0) // a button without a key shows only its name
        {
            GameFont.DrawUi(key, x, textY, FontSize, new Color(KeyColor.R, KeyColor.G, KeyColor.B, alpha));
            x += GameFont.UiWidth(key, FontSize) + 7 * Scale;
        }
        GameFont.DrawUi(DynamicText?.Invoke() ?? Text, x, textY, FontSize, new Color((byte)235, (byte)235, (byte)235, alpha));
    }
}
