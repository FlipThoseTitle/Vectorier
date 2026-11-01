using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Vectorier.Services.IO;
using Vectorier.Services.Xml;

namespace Vectorier.Services.Triggers;

/// <summary>
/// {project}/xmlroot/TriggerTemplates.xml opened for editing: the list of &lt;Template&gt;s, their order and Library
/// flag, and each template's content. Changes stay in memory until <see cref="SaveAsync"/>. (The read-only
/// <see cref="TriggerTemplateLibrary"/> is what the Trigger Editor uses to describe and pick templates.)
/// </summary>
public sealed class TriggerTemplateFile
{
    private readonly XDocument _document;

    private TriggerTemplateFile(string path, XDocument document)
    {
        Path = path;
        _document = document;
    }

    public string Path { get; }

    private XElement Root => _document.Root!;

    /// <summary>The &lt;Template&gt; elements in file order.</summary>
    public IReadOnlyList<XElement> Templates => Root.Elements("Template").ToList();

    public static string PathFor(Projects.ProjectInfo project) => VPath.Combine(project.XmlRoot, "TriggerTemplates.xml");

    public static async Task<TriggerTemplateFile> LoadAsync(IFileSystem fs, string path)
    {
        XDocument document;
        if (await fs.FileExistsAsync(path))
            document = XmlHelpers.ParseDocument(await fs.ReadAllBytesAsync(path));
        else
            document = new XDocument(new XElement("Templates"));
        if (document.Root == null || document.Root.Name.LocalName != "Templates")
            throw new InvalidOperationException("The file's root element is not <Templates>.");
        return new TriggerTemplateFile(path, document);
    }

    public async Task SaveAsync(IFileSystem fs) => await fs.WriteAllBytesAsync(Path, XmlHelpers.ToBytes(_document));

    public static string NameOf(XElement template) => (string?)template.Attribute("Name") ?? string.Empty;

    public static bool IsLibrary(XElement template) => (string?)template.Attribute("Library") == "1";

    /// <summary>Library="1", or no attribute at all for an ordinary template.</summary>
    public static void SetLibrary(XElement template, bool library) => template.SetAttributeValue("Library", library ? "1" : null);

    /// <summary>Moves a template one place up (-1) or down (+1) among the templates. Comments stay where they are.</summary>
    public bool Move(XElement template, int direction)
    {
        var list = Templates.ToList();
        int index = list.IndexOf(template);
        int target = index + direction;
        if (index < 0 || target < 0 || target >= list.Count)
            return false;
        var other = list[target];
        template.Remove();
        if (direction < 0) other.AddBeforeSelf(template);
        else other.AddAfterSelf(template);
        return true;
    }

    /// <summary>The template's content as the Trigger Editor reads it (&lt;Init&gt; and &lt;Loop&gt;s).</summary>
    public static string ContentOf(XElement template) => XmlHelpers.FormatFragment(template.Nodes());

    /// <summary>Replaces the template's content (its attributes stay).</summary>
    public static void SetContent(XElement template, string contentXml)
    {
        template.RemoveNodes();
        foreach (var node in XmlHelpers.ParseFragment(contentXml))
            template.Add(node);
    }
}
