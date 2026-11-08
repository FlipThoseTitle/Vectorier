using Avalonia;

namespace Vectorier.Models.Scene;

/// <summary>Base for elements that are a single point (Camera, Spawn, Item, Model).</summary>
public abstract class PointElement : SceneElement
{
    public override bool HasSize => false;
    public override bool LayerEditable => false;

    /// <summary>Half-size of the box used for picking and selection outlines.</summary>
    public virtual Size HandleSize => new(80, 80);

    public override Point[] GetLocalCorners()
    {
        var s = HandleSize;
        double hw = s.Width / 2, hh = s.Height / 2;
        return new[]
        {
            new Point(X - hw, Y - hh),
            new Point(X + hw, Y - hh),
            new Point(X + hw, Y + hh),
            new Point(X - hw, Y + hh),
        };
    }

    public override void ScaleAround(Point pivot, double sx, double sy)
    {
        X = pivot.X + (X - pivot.X) * sx;
        Y = pivot.Y + (Y - pivot.Y) * sy;
    }
}
