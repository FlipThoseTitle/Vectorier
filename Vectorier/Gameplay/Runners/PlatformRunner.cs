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
using Vectorier.Gameplay.Level;

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
            if (!IsEnabled)
                return;

            var vs = Sets.Current.Containers[1];
            var pos = vs.TransformPoint(rectangle.Origin);
            Raylib.DrawRectangleV(pos, rectangle.Size * WorldScale, new Color(0, 0, 255, 64));
        }
    }
}
