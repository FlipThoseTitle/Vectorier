using Avalonia;

namespace Vectorier.Models.Scene;

/// <summary>&lt;Camera&gt;: only its position is stored.</summary>
public sealed class CameraElement : PointElement
{
    /// <summary>Visible area of the Unity orthographic camera (size 400 at 16:9).</summary>
    public static readonly Size ViewSize = new(800.0 * 16.0 / 9.0, 800.0);

    public override ElementType Type => ElementType.Camera;
    public override bool NameEditable => false;
    public override bool SupportsDynamics => false;
    public override Size HandleSize => new(120, 120);

    protected override SceneElement CreateEmpty() => new CameraElement();
}
