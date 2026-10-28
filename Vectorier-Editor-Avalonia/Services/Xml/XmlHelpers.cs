using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Avalonia.Media;
using Vectorier_Editor_Avalonia.Models.Scene;

namespace Vectorier_Editor_Avalonia.Services.Xml;

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

public static class XmlHelpers
{
    public static XDocument ParseDocument(string text)
    {
        // Some files ship with a BOM or leading whitespace before the declaration.
        text = text.TrimStart('﻿', ' ', '\t', '\r', '\n');
        return XDocument.Parse(text, LoadOptions.None);
    }

    public static XDocument ParseDocument(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return ParseDocument(reader.ReadToEnd());
    }

    /// <summary>Writes XML with two-space indentation, self-closing empty elements and a UTF-8 declaration.</summary>
    public static string ToText(XDocument document)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\r\n",
            NewLineHandling = NewLineHandling.Replace,
            OmitXmlDeclaration = true,
            Encoding = new UTF8Encoding(false),
        };

        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n");
        using (var stringWriter = new StringWriter(builder, CultureInfo.InvariantCulture))
        using (var writer = XmlWriter.Create(stringWriter, settings))
        {
            document.Root?.WriteTo(writer);
        }
        builder.Append("\r\n");
        return builder.ToString();
    }

    public static byte[] ToBytes(XDocument document) => new UTF8Encoding(false).GetBytes(ToText(document));

    /// <summary>Pretty-prints a fragment (used for trigger content shown in the editor).</summary>
    public static string FormatFragment(IEnumerable<XNode> nodes)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
            OmitXmlDeclaration = true,
            ConformanceLevel = ConformanceLevel.Fragment,
        };

        var builder = new StringBuilder();
        using (var stringWriter = new StringWriter(builder, CultureInfo.InvariantCulture))
        using (var writer = XmlWriter.Create(stringWriter, settings))
        {
            foreach (var node in nodes)
                node.WriteTo(writer);
        }
        return builder.ToString();
    }

    /// <summary>Parses a fragment such as trigger content. Throws <see cref="XmlException"/> when invalid.</summary>
    public static List<XNode> ParseFragment(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<XNode>();
        var wrapper = XElement.Parse("<Fragment>" + text + "</Fragment>", LoadOptions.None);
        return wrapper.Nodes().ToList();
    }

    public static bool TryValidateFragment(string text, out string? error)
    {
        try
        {
            ParseFragment(text);
            error = null;
            return true;
        }
        catch (XmlException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    // ---- Colors are written by Unity as #RRGGBBAA ----

    public static bool TryParseRgba(string? text, out Color color)
    {
        color = Colors.White;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string hex = text.Trim().TrimStart('#');
        if (hex.Length != 6 && hex.Length != 8)
            return false;
        if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value))
            return false;

        if (hex.Length == 6)
        {
            color = Color.FromArgb(255, (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        }
        else
        {
            color = Color.FromArgb((byte)value, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8));
        }
        return true;
    }

    public static string ToRgba(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";
}
