using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia;
using Vectorier_Editor_Avalonia.Models.Scene;
using Vectorier_Editor_Avalonia.Services.IO;
using Vectorier_Editor_Avalonia.Services.Projects;

namespace Vectorier_Editor_Avalonia.Services.Xml;

/// <summary>
/// File-level operations on Object XML files (xmlroot): Building attributes, replacing a definition, and renaming a
/// definition with every reference to it in the project.
/// </summary>
public static class ObjectFileOps
{
    // ================= BUILDINGS ================= //

    /// <summary>
    /// In / Out / Box of a Building definition from its content (definition space). In defaults to the leftmost
    /// Platform's top-left, Out to the rightmost Platform's top-right. The Box is the union of the Platforms,
    /// Triggers, Areas and Trapezoids; it is not written for a single child or a single image.
    /// </summary>
    public static (Point In, Point Out, Rect? Box) ComputeBuilding(IReadOnlyList<SceneElement> content, Point? placedIn, Point? placedOut)
    {
        var platforms = new List<Rect>();
        Rect? box = null;
        foreach (var (element, offset) in Flatten(content, default, 0))
        {
            if (element is not (PlatformElement or TriggerElement or AreaElement or TrapezoidElement))
                continue;
            var b = element.GetLocalBounds().Translate(offset);
            box = box == null ? b : box.Value.Union(b);
            if (element is PlatformElement)
                platforms.Add(b);
        }

        var left = platforms.OrderBy(p => p.Left).ThenBy(p => p.Top).FirstOrDefault();
        var right = platforms.OrderByDescending(p => p.Right).ThenBy(p => p.Top).FirstOrDefault();
        var autoIn = platforms.Count > 0 ? left.TopLeft : new Point(0, 0);
        var autoOut = platforms.Count > 0 ? right.TopRight : new Point(box?.Right ?? 0, 0);

        // One child (a single image, for example): only In and Out are written.
        bool single = content.Count(c => c is not BuildingMarkerElement) <= 1;
        return (placedIn ?? autoIn, placedOut ?? autoOut, single ? null : box);
    }

    /// <summary>Every element under the content with its offset to definition space (inline children and referenced content).</summary>
    private static IEnumerable<(SceneElement Element, Vector Offset)> Flatten(IEnumerable<SceneElement> elements, Vector offset, int depth)
    {
        if (depth > 32)
            yield break;
        foreach (var e in elements)
        {
            if (e is BuildingMarkerElement)
                continue;
            yield return (e, offset);
            if (e is ObjectElement obj)
            {
                var inner = offset + new Vector(obj.X, obj.Y);
                foreach (var x in Flatten(obj.Children, inner, depth + 1))
                    yield return x;
                if (obj.Reference is { } def)
                    foreach (var x in Flatten(def.Content, inner, depth + 1))
                        yield return x;
            }
        }
    }

    public static void WriteBuildingAttributes(XElement definition, Point inPoint, Point outPoint, Rect? box)
    {
        RemoveBuildingAttributes(definition);
        definition.SetAttributeValue("InX", Num.Format(inPoint.X));
        definition.SetAttributeValue("InY", Num.Format(inPoint.Y));
        definition.SetAttributeValue("OutX", Num.Format(outPoint.X));
        definition.SetAttributeValue("OutY", Num.Format(outPoint.Y));
        if (box is { } b)
        {
            definition.SetAttributeValue("BoxX", Num.Format(b.X));
            definition.SetAttributeValue("BoxY", Num.Format(b.Y));
            definition.SetAttributeValue("BoxWidth", Num.Format(b.Width));
            definition.SetAttributeValue("BoxHeight", Num.Format(b.Height));
        }
    }

    public static void RemoveBuildingAttributes(XElement definition)
    {
        foreach (var a in ObjectDefinition.BuildingAttributes)
            definition.Attribute(a)?.Remove();
    }

    // ================= FILES ================= //

    public static async Task<XDocument> ReadAsync(string path) => XmlHelpers.ParseDocument(await AppServices.FileSystem.ReadAllBytesAsync(path));

    public static Task WriteAsync(string path, XDocument doc) => AppServices.FileSystem.WriteAllBytesAsync(path, XmlHelpers.ToBytes(doc));

    /// <summary>The definition element named <paramref name="name"/> in an Object XML (top level of &lt;Objects&gt;).</summary>
    public static XElement? FindDefinition(XDocument doc, string name) =>
        doc.Root?.Element("Objects")?.Elements("Object").FirstOrDefault(o => (string?)o.Attribute("Name") == name);

    /// <summary>Object XML files of the project (xmlroot, without TriggerTemplates.xml).</summary>
    public static async Task<List<string>> ObjectFilesAsync(ProjectInfo project) =>
        (await AppServices.FileSystem.GetFilesAsync(project.XmlRoot))
        .Where(f => VPath.HasExtension(f, ".xml") && !string.Equals(VPath.GetFileName(f), "TriggerTemplates.xml", StringComparison.OrdinalIgnoreCase))
        .ToList();

    /// <summary>
    /// Fast check on the raw text: does the file's &lt;Sets&gt; list <paramref name="file"/>? Files without
    /// &lt;Sets&gt; are skipped without parsing.
    /// </summary>
    public static bool SetsReference(byte[] data, string file)
    {
        string text = Encoding.Latin1.GetString(data);
        int start = text.IndexOf("<Sets", StringComparison.Ordinal);
        if (start < 0)
            return false;
        int end = text.IndexOf("</Sets>", start, StringComparison.Ordinal);
        if (end < 0)
            end = Math.Min(text.Length, start + 4000);
        return text.AsSpan(start, end - start).IndexOf(file.AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static int RenameObjects(XContainer root, string oldName, string newName)
    {
        int count = 0;
        foreach (var obj in root.Descendants("Object"))
        {
            var attr = obj.Attribute("Name");
            if (attr != null && attr.Value == oldName)
            {
                attr.Value = newName;
                count++;
            }
        }
        return count;
    }

    /// <summary>Files changed by a rename (paths).</summary>
    public sealed record RenameReport(List<string> ChangedFiles);

    /// <summary>
    /// Renames a definition of <paramref name="file"/> and every reference to it: the file itself, then the other
    /// Object XML files and the levels whose &lt;Sets&gt; list the file.
    /// </summary>
    public static async Task<RenameReport> RenameDefinitionAsync(ProjectInfo project, string file, string oldName, string newName)
    {
        var fs = AppServices.FileSystem;
        var changed = new List<string>();

        string sourcePath = VPath.Combine(project.XmlRoot, file);
        var source = await ReadAsync(sourcePath);
        if (RenameObjects(source, oldName, newName) > 0)
        {
            await WriteAsync(sourcePath, source);
            changed.Add(sourcePath);
        }

        var others = (await ObjectFilesAsync(project)).Where(p => !string.Equals(VPath.GetFileName(p), file, StringComparison.OrdinalIgnoreCase)).ToList();
        var levels = (await fs.GetFilesAsync(project.LevelsFolder)).Where(p => VPath.HasExtension(p, ".xml")).ToList();
        foreach (var (path, isLevel) in others.Select(p => (p, false)).Concat(levels.Select(p => (p, true))))
        {
            byte[] data;
            try { data = await fs.ReadAllBytesAsync(path); }
            catch { continue; }
            if (!SetsReference(data, file))
                continue;
            var doc = XmlHelpers.ParseDocument(data);
            int count = 0;
            if (isLevel)
            {
                // Level objects named like the definition are its instances.
                if (doc.Root != null)
                    count += RenameObjects(doc.Root, oldName, newName);
            }
            else
            {
                // Object XML: only references inside definitions (other definitions keep their own names).
                foreach (var definition in doc.Root?.Element("Objects")?.Elements("Object") ?? Enumerable.Empty<XElement>())
                    if (definition.Element("Content") is { } content)
                        count += RenameObjects(content, oldName, newName);
            }
            if (count > 0)
            {
                await WriteAsync(path, doc);
                changed.Add(path);
            }
        }
        return new RenameReport(changed);
    }
}
