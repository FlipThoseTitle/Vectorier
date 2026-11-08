using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Avalonia;

namespace Vectorier.Models.Scene;

/// <summary>
/// A named object from an objects/buildings XML (a "prefab"). Its content is built once into read-only
/// scene elements, positioned relative to the instance.
/// </summary>
public sealed class ObjectDefinition
{
    public ObjectDefinition(string name, string sourceFile, XElement xml)
    {
        Name = name;
        SourceFile = sourceFile;
        _xml = xml;
        _header = xml;
    }

    /// <summary>
    /// A definition of an object file, kept as text until it is built: a parsed tree of every object file of a
    /// project takes several times their size. <paramref name="header"/> is the Object element with its attributes only.
    /// </summary>
    public ObjectDefinition(string name, string sourceFile, XElement header, string xmlText)
    {
        Name = name;
        SourceFile = sourceFile;
        _header = header;
        _text = xmlText;
    }

    private readonly XElement? _xml;
    private readonly XElement _header;
    private readonly string? _text;

    public string Name { get; }
    public string SourceFile { get; }

    /// <summary>The whole Object element (parsed from its text each time it is asked for: only building it needs it).</summary>
    public XElement Xml => _xml ?? XElement.Parse(_text!);

    /// <summary>Building entry / exit (InX, InY / OutX, OutY), relative to the instance; null for plain objects.</summary>
    public Point? In => Point2("InX", "InY");
    public Point? Out => Point2("OutX", "OutY");

    /// <summary>A Building definition: it has any of the In / Out / Box attributes.</summary>
    public bool IsBuilding => BuildingAttributes.Any(a => _header.Attribute(a) != null);

    public static readonly string[] BuildingAttributes = { "InX", "InY", "OutX", "OutY", "BoxX", "BoxY", "BoxWidth", "BoxHeight" };

    private Point? Point2(string x, string y)
    {
        if (_header.Attribute(x) == null && _header.Attribute(y) == null)
            return null;
        return new Point(Num.Parse((string?)_header.Attribute(x)), Num.Parse((string?)_header.Attribute(y)));
    }

    /// <summary>
    /// The definition's own Dynamic transformations (its &lt;Properties&gt;&lt;Dynamic&gt;): the game runs them on every
    /// instance, with the instance's own (e.g. objects_techpark.xml's HighwayCargo, placed in a building).
    /// </summary>
    public List<DynamicTransformation> Dynamics { get; set; } = new();

    /// <summary>Resolved content (built lazily by the importer).</summary>
    public List<SceneElement> Content { get; set; } = new();

    public bool IsBuilt { get; set; }

    /// <summary>Bounds of the content relative to the instance position.</summary>
    public Rect? Bounds { get; set; }

    public void ComputeBounds()
    {
        Rect? union = null;
        foreach (var element in Content)
        {
            var b = element.GetLocalBounds();
            if (b.Width <= 0 && b.Height <= 0 && element is UnknownElement)
                continue;
            union = union == null ? b : union.Value.Union(b);
        }
        Bounds = union;
    }
}
