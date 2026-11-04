// Ported from Vector-Raylib-For-Vectorier (the game's player movement and model drawing).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Vectorier.Gameplay.Location;
using Vectorier.Gameplay.Node;

namespace Vectorier.Gameplay
{
    public class Triangle
    {
        public ModelNode Node0;

        public ModelNode Node1;

        public ModelNode Node2;

        public bool IsVisible;

        /// <summary>The game's Triangle.Draw: the three nodes filled (both faces).</summary>
        public void Draw(Models.ModelCanvas canvas)
        {
            if (!IsVisible) return;

            var a = canvas.Transform(Node0.Start);
            var b = canvas.Transform(Node1.Start);
            var c = canvas.Transform(Node2.Start);

            canvas.Polygon(a, b, c);
        }
    }
}
