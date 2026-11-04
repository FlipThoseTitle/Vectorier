// Ported from Vector-Raylib-For-Vectorier (the game's player movement), see Gameplay/README.md.
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace Vectorier.Gameplay.Utilities
{
    public static class Math
    {
        public static double Round(double Value, double Pow)
        {
            return System.Math.Floor(Value * Pow + 0.5) / Pow;
        }

        public static float Round(float Value, float Pow)
        {
            return MathF.Floor(Value * Pow + 0.5f) / Pow;
        }
    }
}
