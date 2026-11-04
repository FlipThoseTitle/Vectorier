// Stand-ins for the few Raylib types the game's movement code names (colors, keys, drawables). The editor
// draws the character itself, so nothing here renders.
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;

namespace Vectorier.Gameplay
{
    /// <summary>An RGBA color (Raylib's Color in the game).</summary>
    public readonly struct Color : IEquatable<Color>
    {
        public readonly byte R, G, B, A;

        public Color(byte r, byte g, byte b, byte a)
        {
            R = r; G = g; B = b; A = a;
        }

        public Color(int r, int g, int b, int a) : this((byte)r, (byte)g, (byte)b, (byte)a) { }

        public static readonly Color Black = new(0, 0, 0, 255);
        public static readonly Color White = new(255, 255, 255, 255);
        public static readonly Color Red = new(230, 41, 55, 255);
        public static readonly Color Green = new(0, 228, 48, 255);
        public static readonly Color Blue = new(0, 121, 241, 255);
        public static readonly Color Yellow = new(253, 249, 0, 255);

        public bool Equals(Color other) => R == other.R && G == other.G && B == other.B && A == other.A;
        public override bool Equals(object obj) => obj is Color c && Equals(c);
        public override int GetHashCode() => HashCode.Combine(R, G, B, A);
    }

    /// <summary>The keys the moves react to (Raylib's KeyboardKey in the game).</summary>
    public enum KeyboardKey
    {
        Null,
        Up,
        Down,
        Left,
        Right,
    }

    public interface IDrawable
    {
        float Z { get; set; }

        void Draw();
    }
}
