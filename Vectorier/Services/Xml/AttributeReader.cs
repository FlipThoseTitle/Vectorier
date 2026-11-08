using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Vectorier.Models.Scene;

namespace Vectorier.Services.Xml;

/// <summary>Reads attributes while remembering which ones were consumed, so the rest can be preserved.</summary>
internal sealed class AttributeReader
{
    private readonly XElement _element;
    private readonly HashSet<string> _consumed = new(StringComparer.Ordinal);

    public AttributeReader(XElement element) => _element = element;

    public bool Has(string name) => _element.Attribute(name) != null;

    public string? String(string name)
    {
        _consumed.Add(name);
        return _element.Attribute(name)?.Value;
    }

    public double Double(string name, double fallback = 0) => Num.Parse(String(name), fallback);

    public int Int(string name, int fallback = 0) => Num.ParseInt(String(name), fallback);

    public void Consume(params string[] names)
    {
        foreach (var name in names)
            _consumed.Add(name);
    }

    public Dictionary<string, string> Remaining()
    {
        var dict = new Dictionary<string, string>();
        foreach (var attribute in _element.Attributes())
        {
            string name = attribute.Name.LocalName;
            if (!_consumed.Contains(name) && !attribute.IsNamespaceDeclaration)
                dict[name] = attribute.Value;
        }
        return dict;
    }
}
