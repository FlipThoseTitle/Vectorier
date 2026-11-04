// Ported from Vector-Raylib-For-Vectorier (the game's player movement), see Gameplay/README.md.
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Vectorier.Gameplay.Location;
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

        public void Draw(Color color)
        {
            // Drawing is done by the editor (Controls/Scene/SceneView.Gameplay.cs).
        }
    }
}
