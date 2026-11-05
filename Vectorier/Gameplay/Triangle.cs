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
using System.Numerics;
using Vectorier.Gameplay.Level;
using Vectorier.Gameplay.Node;

namespace Vectorier.Gameplay
{
    public class Triangle
    {
        public ModelNode Node0;

        public ModelNode Node1;

        public ModelNode Node2;

        public bool IsVisible;

        public void Draw(Color color)
        {
            if (!IsVisible) return;

            var vs = Sets.Current.Containers[1];

            Vector2 a = vs.TransformPoint(Node0.Start);
            Vector2 b = vs.TransformPoint(Node1.Start);
            Vector2 c = vs.TransformPoint(Node2.Start);

            Rlgl.DisableBackfaceCulling();
            Rlgl.Begin(DrawMode.Triangles);

            Rlgl.Color4ub(color.R, color.G, color.B, color.A);

            Rlgl.Vertex2f(a.X, a.Y);
            Rlgl.Vertex2f(b.X, b.Y);
            Rlgl.Vertex2f(c.X, c.Y);

            Rlgl.End();
        }

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
