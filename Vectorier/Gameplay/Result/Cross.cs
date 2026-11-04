// Ported from Vector-Raylib-For-Vectorier (the game's player movement), see Gameplay/README.md.
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Vectorier.Gameplay.Result
{
    public struct Cross
    {
        private int _Index;

        private Vector3 _Point;

        public int Index => _Index;

        public Vector3 Point => _Point;

        public Cross(Vector3 point, int index)
        {
            _Index = index;
            _Point = point;
        }
    }
}
