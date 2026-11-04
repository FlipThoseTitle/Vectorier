// Ported from Vector-Raylib-For-Vectorier (the game's player movement and model drawing).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Vectorier.Gameplay.Location;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Node;

namespace Vectorier.Gameplay.Geometry
{
    public class Capsule
    {
        public ModelLine Base;

        public float Stroke = 1;

        public string Name => Base.Name;

        public bool IsVisible = false;

        public void Init(ModelLine modelLine)
        {
            Base = modelLine;
            Stroke = Base.Stroke;
        }

        /// <summary>
        /// The game's Capsule.Draw: a circle of the capsule's radius at each end (moved in by the margins) joined by a
        /// line twice as thick. <paramref name="canvas"/> stands in for Raylib and the location's transform.
        /// </summary>
        public void Draw(ModelCanvas canvas)
        {
            if (!IsVisible)
            {
                return;
            }

            var start = canvas.Transform(Base.Start.Start);
            var start2 = canvas.Transform(Base.End.Start);
            double num = start.X - start2.X;
            double num2 = start.Y - start2.Y;
            double num3 = start.X - num * Base.Margin1;
            double num4 = start.Y - num2 * Base.Margin1;
            double num5 = start2.X + num * Base.Margin2;
            double num6 = start2.Y + num2 * Base.Margin2;

            var pos1 = new Avalonia.Point(num3, num4);
            var pos2 = new Avalonia.Point(num5, num6);

            canvas.Circle(pos1, Stroke * canvas.Scale);
            canvas.Circle(pos2, Stroke * canvas.Scale);

            canvas.Line(pos1, pos2, Stroke * 2 * canvas.Scale);
        }
    }
}
