#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;

namespace Vectorier.Gameplay.Diagnostics;

/// <summary>
/// Where the game's XML comes from, so an error found while reading it can name the file, the element (and its
/// line) and the attribute. The game reads its XML into XmlDocuments, which keep neither the file nor the lines:
/// <see cref="Register"/> remembers both for every document read, and the line of an element is found again from
/// the text when an error needs it.
/// </summary>
public static class XmlSource
{
    private sealed class Source
    {
        public required string File;
        public required string Text;
    }

    private static readonly ConditionalWeakTable<XmlDocument, Source> Sources = new();

    /// <summary>The document was read from <paramref name="file"/> (its game path) with this text.</summary>
    public static void Register(XmlDocument document, string file, string text)
    {
        Sources.AddOrUpdate(document, new Source { File = file, Text = text });
    }

    /// <summary>The file the node was read from (null when it was not read from one).</summary>
    public static string? FileOf(XmlNode? node)
    {
        var document = node as XmlDocument ?? node?.OwnerDocument;
        return document != null && Sources.TryGetValue(document, out var source) ? source.File : null;
    }

    // ================= ELEMENTS ================= //

    /// <summary>A node as the error names it: &lt;Move Name="MonkeyToVault"&gt; (its name, or what tells it apart).</summary>
    public static string Describe(XmlNode node)
    {
        var sb = new StringBuilder("<").Append(node.Name);
        foreach (var attribute in IdentifyingAttributes)
        {
            if (node.Attributes?[attribute]?.Value is { Length: > 0 } value)
            {
                sb.Append(' ').Append(attribute).Append("=\"").Append(Shorten(value)).Append('"');
                break;
            }
        }
        return sb.Append('>').ToString();
    }

    private static readonly string[] IdentifyingAttributes = { "Name", "FileName", "Key", "Type", "ClassName" };

    private static string Shorten(string value) => value.Length > 60 ? value[..57] + "..." : value;

    /// <summary>The node's place from the document's root: root › Moves › &lt;Move Name="..."&gt; › Interval #3.</summary>
    public static string PathOf(XmlNode node)
    {
        var parts = new List<string>();
        for (var n = node; n != null && n is not XmlDocument; n = n.ParentNode)
        {
            if (n is not XmlElement)
                continue;
            string part = Describe(n);
            // Elements without a name among same-named siblings: which one.
            if (part == "<" + n.Name + ">" && n.ParentNode != null)
            {
                int index = 0, count = 0;
                foreach (XmlNode sibling in n.ParentNode.ChildNodes)
                {
                    if (sibling.Name != n.Name)
                        continue;
                    count++;
                    if (ReferenceEquals(sibling, n))
                        index = count;
                }
                if (count > 1)
                    part += " #" + index;
            }
            parts.Add(part);
        }
        parts.Reverse();
        return string.Join(" › ", parts);
    }

    /// <summary>The line and column of the element in its file (0, 0 when it cannot be found).</summary>
    public static (int Line, int Column) Locate(XmlNode? node)
    {
        if (node is XmlAttribute attribute)
            node = attribute.OwnerElement;
        if (node is not XmlElement || node.OwnerDocument is not { } document || !Sources.TryGetValue(document, out var source))
            return (0, 0);

        // Its position among the elements at each level (the document holds no comments or whitespace nodes).
        var path = new List<int>();
        for (var n = node; n != null && n is not XmlDocument; n = n.ParentNode)
        {
            int index = 0;
            for (var s = n.PreviousSibling; s != null; s = s.PreviousSibling)
                if (s is XmlElement)
                    index++;
            path.Add(index);
        }
        path.Reverse();

        try
        {
            using var reader = XmlReader.Create(new StringReader(source.Text),
                new XmlReaderSettings { IgnoreComments = true, DtdProcessing = DtdProcessing.Ignore });
            var info = (IXmlLineInfo)reader;
            int level = 0, seen = 0;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.Depth != level)
                    continue;
                if (seen++ != path[level])
                    continue;
                if (level == path.Count - 1)
                    return (info.LineNumber, info.LinePosition);
                level++;
                seen = 0;
            }
        }
        catch (XmlException)
        {
        }
        return (0, 0);
    }

    // ================= ERRORS ================= //

    private static readonly ConditionalWeakTable<Exception, List<XmlNode>> Notes = new();

    /// <summary>
    /// For an exception filter around the reading of an element:
    /// <c>catch (Exception ex) when (XmlSource.Note(ex, node)) { throw; }</c>. It remembers that the error happened
    /// while <paramref name="node"/> was read and returns false, so nothing is caught and the game behaves as it
    /// did. Filters run innermost first: the first node noted is the one the error is in.
    /// </summary>
    public static bool Note(Exception exception, XmlNode? node)
    {
        if (node != null)
        {
            var nodes = Notes.GetOrCreateValue(exception);
            lock (nodes)
                nodes.Add(node);
        }
        return false;
    }

    /// <summary>
    /// The element that was being read when the error happened in <paramref name="node"/> but that does not contain
    /// it: what uses a shared definition (the move's interval using an event group, the object placing a set's
    /// object). Null when the error's element is inside everything noted.
    /// </summary>
    public static XmlNode? UserOf(Exception exception, XmlNode node)
    {
        if (!Notes.TryGetValue(exception, out var nodes))
            return null;
        lock (nodes)
        {
            foreach (var noted in nodes)
            {
                bool ancestor = false;
                for (var n = node; n != null; n = n.ParentNode)
                {
                    if (ReferenceEquals(n, noted))
                    {
                        ancestor = true;
                        break;
                    }
                }
                if (!ancestor)
                    return noted;
            }
        }
        return null;
    }

    /// <summary>The innermost element noted for the exception (null when none was).</summary>
    public static XmlNode? NodeOf(Exception exception)
    {
        if (!Notes.TryGetValue(exception, out var nodes))
            return null;
        lock (nodes)
            return nodes.Count > 0 ? nodes[0] : null;
    }

    // ================= REQUIRED DATA ================= //

    /// <summary>The attribute's value; a <see cref="GameDataException"/> naming it when the element has none.</summary>
    public static string Required(XmlNode node, string attribute)
    {
        return node.Attributes?[attribute]?.Value
            ?? throw new GameDataException($"The required attribute \"{attribute}\" is missing.", node: node, attribute: attribute);
    }

    /// <summary>The child element; a <see cref="GameDataException"/> naming it when the element has none.</summary>
    public static XmlElement RequiredChild(XmlNode node, string child)
    {
        return node[child]
            ?? throw new GameDataException($"The required element <{child}> is missing.", node: node);
    }
}
