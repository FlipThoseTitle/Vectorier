// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
