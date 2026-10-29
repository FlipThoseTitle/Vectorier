using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace Vectorier.Models.Scene;

/// <summary>
/// One &lt;Transformation&gt; inside &lt;Properties&gt;&lt;Dynamic&gt;. Values are stored exactly as in XML
/// (Y pointing down, angles in Vector's sign), the UI converts when displaying.
/// </summary>
public sealed class DynamicTransformation
{
    public string Name { get; set; } = "Transform";
    public List<DynamicMove> Moves { get; set; } = new();
    public List<DynamicSize> Sizes { get; set; } = new();
    public List<DynamicRotation> Rotations { get; set; } = new();
    public List<DynamicColor> Colors { get; set; } = new();

    public int TotalFrames
    {
        get
        {
            int move = 0;
            foreach (var m in Moves)
                move += (int)System.Math.Round(m.Delay) + m.Frames;
            int size = Sizes.Sum(s => s.Frames);
            int rot = Rotations.Sum(r => r.Frames);
            int col = Colors.Sum(c => c.Frames);
            return new[] { move, size, rot, col }.Max();
        }
    }

    public DynamicTransformation Clone() => new()
    {
        Name = Name,
        Moves = Moves.Select(m => m with { }).ToList(),
        Sizes = Sizes.Select(s => s with { }).ToList(),
        Rotations = Rotations.Select(r => r with { }).ToList(),
        Colors = Colors.Select(c => c with { }).ToList(),
    };
}

/// <summary>A MoveInterval: relative movement to Finish over Frames, curved through Support (quadratic Bézier).</summary>
public sealed record DynamicMove
{
    public int Frames { get; set; } = 30;
    public double Delay { get; set; }
    public double FinishX { get; set; }
    public double FinishY { get; set; }
    public double SupportX { get; set; }
    public double SupportY { get; set; }
}

public sealed record DynamicSize
{
    public int Frames { get; set; } = 30;
    public double FinalWidth { get; set; }
    public double FinalHeight { get; set; }
}

public sealed record DynamicRotation
{
    /// <summary>Angle in degrees as written in XML.</summary>
    public double Angle { get; set; }
    public double AnchorX { get; set; }
    public double AnchorY { get; set; }
    public int Frames { get; set; } = 30;
}

public sealed record DynamicColor
{
    public Color Start { get; set; } = Colors.White;
    public Color Finish { get; set; } = Colors.White;
    public int Frames { get; set; } = 30;
}
