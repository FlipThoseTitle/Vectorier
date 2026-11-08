using Avalonia;

namespace Vectorier.Models.Scene;

/// <summary>
/// In / Out point of a Building definition, edited in Edit Object mode. Not an XML element: written as the
/// definition's InX / InY / OutX / OutY. Center pivot, cannot be renamed, never listed in the Hierarchy.
/// </summary>
public sealed class BuildingMarkerElement : PointElement
{
    public BuildingMarkerElement(bool isIn)
    {
        IsIn = isIn;
        Name = isIn ? "In" : "Out";
    }

    public bool IsIn { get; }
    public override ElementType Type => IsIn ? ElementType.BuildingIn : ElementType.BuildingOut;
    public override bool NameEditable => false;
    public override bool SupportsDynamics => false;
    public override Size HandleSize => new(100, 100);

    protected override SceneElement CreateEmpty() => new BuildingMarkerElement(IsIn);
}
