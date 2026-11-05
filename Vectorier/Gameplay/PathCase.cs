// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace Vectorier.Gameplay
{
    /// <summary>
    /// Linux and macOS file systems can be case-sensitive while Vector's XML names files the Windows way
    /// ("Buildings.xml" for buildings.xml). Resolve finds the file with any casing; on Windows it does nothing.
    /// </summary>
    /// <summary>The game's file names are found whatever their casing: the editor's store (GameFiles) ignores case.</summary>
    public static class PathCase
    {
        public static void Clear() { }

        public static string Resolve(string path) => path;
    }
}
