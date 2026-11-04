// Ported from Vector-Raylib-For-Vectorier (the game's player movement and model drawing).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace Vectorier.Gameplay.Animation
{
    public enum AnimationDeltaType
    {
        Vertical = 1,
        Horizontal,
        Collision,
        Max
    }
}
