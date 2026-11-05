// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
