// Ported from Vector-Raylib-For-Vectorier (the game's player movement), see Gameplay/README.md.
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace Vectorier.Gameplay
{
    public enum GameEndType
    {
        GE_NONE,
        GE_VICTORT,
        GE_DEATH,
        GE_LOSS,
        GE_ARREST,
        GE_RESET,
        GE_EXIT,
        GE_RELOAD,
        GE_MURDER
    }
}
