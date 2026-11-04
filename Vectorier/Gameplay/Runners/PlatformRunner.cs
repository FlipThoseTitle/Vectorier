// Ported from Vector-Raylib-For-Vectorier (the game's player movement and model drawing).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vectorier.Gameplay.Location;

namespace Vectorier.Gameplay.Runners
{
    public class PlatformRunner : QuadRunner
    {
        public PlatformRunner(string name, float x, float y, float width, float height, bool sticky) : base(x, y, width, height, sticky, 0, name)
        {
            Z = -11;
            //GlobalObjects.AddDrawable(this);
        }

        public override void Draw()
        {
            // Drawing is done by the editor (Controls/Scene/SceneView.Gameplay.cs).
        }
    }
}
