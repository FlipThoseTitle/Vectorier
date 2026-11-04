// Ported from Vector-Raylib-For-Vectorier (the game's player movement), see Gameplay/README.md.
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using System.Numerics;

namespace Vectorier.Gameplay.Frame
{
    public class Provider
    {
        public Vector3[][] Data
        {
            get;
        }

        public int Length => Data.Length;

        public Vector3[] this[int p_index] => null;

        public Provider(int count)
        {
            Data = new Vector3[count][];
        }

        public void Add(Vector3[] p_item, int p_index)
        {
            Data[p_index] = p_item;
        }
    }
}
