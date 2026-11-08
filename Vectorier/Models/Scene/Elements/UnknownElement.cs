using System.Xml.Linq;
using Avalonia;

namespace Vectorier.Models.Scene;

/// <summary>An XML element the editor does not understand. Written back exactly as it was read.</summary>
public sealed class UnknownElement : SceneElement
{
    public override ElementType Type => ElementType.Unknown;
    public override bool HasSize => false;
    public override bool CanResize => false;
    public override bool NameEditable => false;
    public override bool SupportsDynamics => false;

    public XElement Xml { get; set; } = new("Unknown");

    public override Point[] GetLocalCorners() => new[] { new Point(X, Y), new Point(X, Y), new Point(X, Y), new Point(X, Y) };

    protected override SceneElement CreateEmpty() => new UnknownElement();

    protected override void CopyTo(SceneElement target)
    {
        base.CopyTo(target);
        ((UnknownElement)target).Xml = new XElement(Xml);
    }
}
