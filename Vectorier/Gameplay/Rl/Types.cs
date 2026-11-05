// The editor's stand-in for Raylib-cs: the types the game uses, drawn and played by the editor (Avalonia drawing,
// the editor's textures, the game audio mixer). Values and names follow Raylib-cs so the game's code is unchanged.
#nullable disable warnings
using System;
using System.Numerics;

namespace Vectorier.Gameplay.Rl
{
    public struct Color : IEquatable<Color>
    {
        public byte R, G, B, A;

        public Color(byte r, byte g, byte b, byte a) { R = r; G = g; B = b; A = a; }
        public Color(int r, int g, int b, int a) : this((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255), (byte)Math.Clamp(a, 0, 255)) { }
        public Color(int r, int g, int b) : this(r, g, b, 255) { }
        public Color(float r, float g, float b, float a) : this((int)r, (int)g, (int)b, (int)a) { }

        public static readonly Color LightGray = new(200, 200, 200, 255);
        public static readonly Color Gray = new(130, 130, 130, 255);
        public static readonly Color DarkGray = new(80, 80, 80, 255);
        public static readonly Color Yellow = new(253, 249, 0, 255);
        public static readonly Color Gold = new(255, 203, 0, 255);
        public static readonly Color Orange = new(255, 161, 0, 255);
        public static readonly Color Pink = new(255, 109, 194, 255);
        public static readonly Color Red = new(230, 41, 55, 255);
        public static readonly Color Maroon = new(190, 33, 55, 255);
        public static readonly Color Green = new(0, 228, 48, 255);
        public static readonly Color Lime = new(0, 158, 47, 255);
        public static readonly Color DarkGreen = new(0, 117, 44, 255);
        public static readonly Color SkyBlue = new(102, 191, 255, 255);
        public static readonly Color Blue = new(0, 121, 241, 255);
        public static readonly Color DarkBlue = new(0, 82, 172, 255);
        public static readonly Color Purple = new(200, 122, 255, 255);
        public static readonly Color Violet = new(135, 60, 190, 255);
        public static readonly Color DarkPurple = new(112, 31, 126, 255);
        public static readonly Color Beige = new(211, 176, 131, 255);
        public static readonly Color Brown = new(127, 106, 79, 255);
        public static readonly Color DarkBrown = new(76, 63, 47, 255);
        public static readonly Color White = new(255, 255, 255, 255);
        public static readonly Color Black = new(0, 0, 0, 255);
        public static readonly Color Blank = new(0, 0, 0, 0);
        public static readonly Color Magenta = new(255, 0, 255, 255);
        public static readonly Color RayWhite = new(245, 245, 245, 255);

        public bool Equals(Color o) => R == o.R && G == o.G && B == o.B && A == o.A;
        public override bool Equals(object obj) => obj is Color c && Equals(c);
        public override int GetHashCode() => R | G << 8 | B << 16 | A << 24;
        public static bool operator ==(Color a, Color b) => a.Equals(b);
        public static bool operator !=(Color a, Color b) => !a.Equals(b);
        public override string ToString() => $"{{R:{R} G:{G} B:{B} A:{A}}}";

        internal uint Argb => (uint)(A << 24 | R << 16 | G << 8 | B);
    }

    public struct Rectangle
    {
        public float X, Y, Width, Height;

        public Rectangle(float x, float y, float width, float height) { X = x; Y = y; Width = width; Height = height; }
        public Rectangle(Vector2 position, Vector2 size) : this(position.X, position.Y, size.X, size.Y) { }

        public Vector2 Position { get => new(X, Y); set { X = value.X; Y = value.Y; } }
        public Vector2 Size { get => new(Width, Height); set { Width = value.X; Height = value.Y; } }
    }

    public struct Camera2D
    {
        public Vector2 Offset, Target;
        public float Rotation, Zoom;

        public Camera2D(Vector2 offset, Vector2 target, float rotation, float zoom)
        {
            Offset = offset; Target = target; Rotation = rotation; Zoom = zoom;
        }
    }

    /// <summary>A texture: an image of the editor (a project texture, an atlas, a built-in picture).</summary>
    public struct Texture2D
    {
        public uint Id;
        public int Width, Height, Mipmaps, Format;

        internal Avalonia.Media.IImage Image => Raylib.TextureImage(Id);
    }

    public struct RenderTexture2D
    {
        public uint Id;
        public Texture2D Texture, Depth;
    }

    /// <summary>A decoded image waiting to become a texture.</summary>
    public struct Image
    {
        public int Width, Height, Mipmaps, Format;
        internal Avalonia.Media.Imaging.Bitmap Bitmap;
    }

    public struct Font
    {
        public int BaseSize, GlyphCount, GlyphPadding;
        public Texture2D Texture;
        internal Avalonia.Media.Typeface? Typeface;
    }

    /// <summary>A sound of the game mixer (the whole WAV decoded).</summary>
    public struct Sound
    {
        public uint FrameCount;
        internal Audio.SoundData Data;
    }

    /// <summary>A music stream of the game mixer.</summary>
    public struct Music
    {
        public uint FrameCount;
        public bool Looping;
        internal Audio.SoundData Data;
    }

    public enum TextureFilter { Point = 0, Bilinear, Trilinear, Anisotropic4X, Anisotropic8X, Anisotropic16X }
    public enum TextureWrap { Repeat = 0, Clamp, MirrorRepeat, MirrorClamp }
    public enum BlendMode { Alpha = 0, Additive, Multiplied, AddColors, SubtractColors, AlphaPremultiply, Custom, CustomSeparate }
    public enum DrawMode { Lines = 0x0001, Triangles = 0x0004, Quads = 0x0007 }
    public enum TraceLogLevel { All = 0, Trace, Debug, Info, Warning, Error, Fatal, None }
    public enum MouseButton { Left = 0, Right = 1, Middle = 2 }

    [Flags]
    public enum ConfigFlags : uint
    {
        VSyncHint = 0x40, FullscreenMode = 0x02, ResizableWindow = 0x04, UndecoratedWindow = 0x08, HiddenWindow = 0x80,
        MinimizedWindow = 0x200, MaximizedWindow = 0x400, UnfocusedWindow = 0x800, TopmostWindow = 0x1000,
        AlwaysRunWindow = 0x100, TransparentWindow = 0x10, HighDpiWindow = 0x2000, MousePassthroughWindow = 0x4000,
        BorderlessWindowMode = 0x8000, Msaa4xHint = 0x20, InterlacedHint = 0x10000,
    }

    /// <summary>Raylib's key codes (only the ones a keyboard gives the game are produced).</summary>
    public enum KeyboardKey
    {
        Null = 0, Apostrophe = 39, Comma = 44, Minus = 45, Period = 46, Slash = 47,
        Zero = 48, One, Two, Three, Four, Five, Six, Seven, Eight, Nine,
        Semicolon = 59, Equal = 61,
        A = 65, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
        LeftBracket = 91, Backslash = 92, RightBracket = 93, Grave = 96,
        Space = 32, Escape = 256, Enter = 257, Tab = 258, Backspace = 259, Insert = 260, Delete = 261,
        Right = 262, Left = 263, Down = 264, Up = 265, PageUp = 266, PageDown = 267, Home = 268, End = 269,
        CapsLock = 280, ScrollLock = 281, NumLock = 282, PrintScreen = 283, Pause = 284,
        F1 = 290, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
        LeftShift = 340, LeftControl = 341, LeftAlt = 342, LeftSuper = 343,
        RightShift = 344, RightControl = 345, RightAlt = 346, RightSuper = 347, KeyboardMenu = 348,
    }
}
