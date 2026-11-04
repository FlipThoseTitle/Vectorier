// Ported from Vector-Raylib-For-Vectorier (the game's player movement), see Gameplay/README.md.
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Grid
{
    public class GridCell
    {
        private List<QuadRunner> _Quads = new List<QuadRunner>();

        public void Add(QuadRunner p_quad)
        {
            _Quads.Add(p_quad);
        }

        public void Remove(QuadRunner p_quad)
        {
            _Quads.Remove(p_quad);
        }

        public void Collect(List<QuadRunner> p_quads)
        {
            for (int i = 0; i < _Quads.Count; i++)
            {
                QuadRunner quadRunner = _Quads[i];
                if (!quadRunner.IsRender)
                {
                    p_quads.Add(quadRunner);
                    quadRunner.IsRender = true;
                }
            }
        }
    }
}
