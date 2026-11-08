using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;

namespace Vectorier.Services.Tutorial;

/// <summary>
/// A tutorial guide drawn in the Scene, in level coordinates of the main layer (Y down, as elements store it):
/// a box (where something goes, or how big it should be) and / or a point marker (where to click).
/// </summary>
public sealed record SceneGuide(Rect? Box = null, Point? Point = null);

/// <summary>The guides the Scene draws over the level while the tutorial shows them.</summary>
public static class SceneGuides
{
    public static IReadOnlyList<SceneGuide> Current { get; private set; } = Array.Empty<SceneGuide>();

    /// <summary>Raised when the guides change (the Scene redraws).</summary>
    public static event Action? Changed;

    public static void Set(IReadOnlyList<SceneGuide> guides)
    {
        if (guides.SequenceEqual(Current))
            return;
        Current = guides.ToArray();
        Changed?.Invoke();
    }

    public static void Clear() => Set(Array.Empty<SceneGuide>());
}
