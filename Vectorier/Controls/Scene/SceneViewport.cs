using Avalonia;

namespace Vectorier.Controls.Scene;

/// <summary>Screen mapping of the Scene view: a level/display point at the view center and a zoom.</summary>
public readonly record struct SceneViewport(Point Center, double Zoom, Size Size)
{

    /// <summary>Parallax offset of a layer: the view center is the reference point (Unity: the camera).</summary>
    public Vector LayerOffset(LayerGroup group) => new(Center.X * (1 - group.Factor * group.FrameScale), Center.Y * (1 - group.Factor * group.FrameScale));

    /// <summary>Level space of a layer → screen pixels.</summary>
    public Matrix LevelToScreen(LayerGroup group)
    {
        double k = group.FrameScale * Zoom;
        var offset = LayerOffset(group);
        double tx = (offset.X - Center.X) * Zoom + Size.Width / 2;
        double ty = (offset.Y - Center.Y) * Zoom + Size.Height / 2;
        return new Matrix(k, 0, 0, k, tx, ty);
    }

    public Point ScreenToLevel(Point screen, LayerGroup group)
    {
        double k = group.FrameScale * Zoom;
        var m = LevelToScreen(group);
        return new Point((screen.X - m.M31) / k, (screen.Y - m.M32) / k);
    }

    /// <summary>Level-space rectangle of a layer that is visible on screen, grown by a margin in pixels.</summary>
    public Rect VisibleLevelRect(LayerGroup group, double marginPixels)
    {
        var a = ScreenToLevel(new Point(-marginPixels, -marginPixels), group);
        var b = ScreenToLevel(new Point(Size.Width + marginPixels, Size.Height + marginPixels), group);
        return RectEx.Box(a, b);
    }
}
