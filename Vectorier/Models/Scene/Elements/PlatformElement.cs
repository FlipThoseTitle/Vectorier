namespace Vectorier.Models.Scene;

/// <summary>&lt;Platform&gt;: rectangular collision. Position and size are integers.</summary>
public sealed class PlatformElement : SceneElement
{
    public override ElementType Type => ElementType.Platform;
    public override bool IntegerTransform => true;
    public override bool LayerEditable => false;

    /// <summary>
    /// Rough / Smooth / Block dressing (Unity's PlatformComponent). Editor only: the textures it generates are
    /// ordinary Images next to the Platform and are rebuilt whenever the Platform or these settings change.
    /// </summary>
    public Services.Assets.PlatformSurfaceSettings? Surface
    {
        get;
        set => SetProperty(ref field, value);
    }

    /// <summary>What the current tiles were built from (<see cref="Services.Assets.PlatformSurface.BuildKey"/>).</summary>
    public string? SurfaceBuiltKey { get; set; }

    protected override SceneElement CreateEmpty() => new PlatformElement();

    protected override void CopyTo(SceneElement target)
    {
        base.CopyTo(target);
        var t = (PlatformElement)target;
        t.Surface = Surface;
        t.SurfaceBuiltKey = SurfaceBuiltKey;
    }
}
