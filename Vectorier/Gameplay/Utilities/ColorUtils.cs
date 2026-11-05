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

namespace Vectorier.Gameplay.Utilities
{
    public static class ColorUtils
    {
        public static Color FromHex(string color, float opacity = 1f)
        {
            color = color.Replace("#", string.Empty);
            if (color.Length != 6 && color.Length != 8)
            {
                return new Color(0f, 0f, 0f, 255f);
            }
            int num = color.Length / 2;
            byte[] array = new byte[4]
            {
                0,
                0,
                0,
                (byte)(255f * opacity)
            };
            for (int i = 0; i < num; i++)
            {
                array[i] = Convert.ToByte(color.Substring(i * 2, 2), 16);
            }
            return new Color(array[0], array[1], array[2], array[3]);
        }

        public static Color Substract(this Color color1, Color color2)
        {
            var color = new Color();
            color.R = (byte)(color1.R - color2.R);
            color.G = (byte)(color1.G - color2.G);
            color.B = (byte)(color1.B - color2.B);
            color.A = (byte)(color1.A - color2.A);
            return color;
        }

        public static Color Substract(this Color color, float num)
        {
            color.R -= (byte)num;
            color.G -= (byte)num;
            color.B -= (byte)num;
            color.A -= (byte)num;
            return color;
        }

        public static Color Add(this Color color1, Color color2)
        {
            var color = new Color();
            color.R = (byte)(color1.R + color2.R);
            color.G = (byte)(color1.G + color2.G);
            color.B = (byte)(color1.B + color2.B);
            color.A = (byte)(color1.A + color2.A);
            return color;
        }

        public static Color Add(this Color color, float num)
        {
            color.R += (byte)num;
            color.G += (byte)num;
            color.B += (byte)num;
            color.A += (byte)num;
            return color;
        }

        public static Color Multiply(this Color color1, Color color2)
        {
            var color = new Color();
            color.R = (byte)(color1.R * color2.R);
            color.G = (byte)(color1.G * color2.G);
            color.B = (byte)(color1.B * color2.B);
            color.A = (byte)(color1.A * color2.A);
            return color;
        }

        public static Color Multiply(this Color color, float num)
        {
            color.R *= (byte)num;
            color.G *= (byte)num;
            color.B *= (byte)num;
            color.A *= (byte)num;
            return color;
        }

        public static Color Divide(this Color color1, Color color2)
        {
            var color = new Color();
            color.R = (byte)(color1.R / color2.R);
            color.G = (byte)(color1.G / color2.G);
            color.B = (byte)(color1.B / color2.B);
            color.A = (byte)(color1.A / color2.A);
            return color;
        }

        public static Color Divide(this Color color, float num)
        {
            color.R /= (byte)num;
            color.G /= (byte)num;
            color.B /= (byte)num;
            color.A /= (byte)num;
            return color;
        }
    }
}
