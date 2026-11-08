using Vectorier.Models.Scene;

namespace Vectorier.Controls.Scene;

/// <summary>Elements sharing one parallax factor. Each layer has its own transform and spatial index.</summary>
public sealed class LayerGroup
{
    public LayerGroup(double factor, int index)
    {
        Factor = factor;
        Index = index;
        FrameScale = Layers.GetFrameScale(factor);
    }

    public double Factor { get; }
    public int Index { get; set; }

    /// <summary>Parallax scale of the layer at zoom 1 (Unity Parallax defaults).</summary>
    public double FrameScale { get; }

    internal readonly SpatialGrid Grid = new();
}
