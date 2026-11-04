// Ported from Vector-Raylib-For-Vectorier (the game's player movement), see Gameplay/README.md.
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace Vectorier.Gameplay
{
    public static class Random
    {
        static readonly System.Random random = new System.Random();
        public static int Range(int min, int max)
        {
            return random.Next(min, max);
        }

        public static float Range(float min, float max)
        {
            return random.NextSingle() * (max - min) + min;
        }
    }
}
