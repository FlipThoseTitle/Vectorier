using System.Threading.Tasks;
using System.Xml.Linq;
using Vectorier.Services.IO;
using Vectorier.Services.Xml;

namespace Vectorier.Services.Projects;

/// <summary>localization/localization_all.xml: display names stored as item_NAME with one attribute per language.</summary>
public sealed class LocalizationService
{
    public static readonly string[] Languages =
    {
        "eng", "rus", "ger", "ita", "fre", "spa", "tur", "por", "jap", "kor",
        "chi1", "chi2", "viet", "hin", "arab", "heb", "thai", "pol", "cze",
        "lat", "dut", "nor", "dan", "finn", "swe", "ukr", "gre"
    };

    private readonly IFileSystem _fs;
    private readonly string _path;
    private XDocument _doc = new(new XElement("log"));

    public LocalizationService(IFileSystem fs, ProjectInfo project)
    {
        _fs = fs;
        _path = VPath.Combine(project.Path, "localization", "localization_all.xml");
    }

    public async Task LoadAsync()
    {
        if (await _fs.FileExistsAsync(_path))
        {
            try { _doc = XmlHelpers.ParseDocument(await _fs.ReadAllBytesAsync(_path)); }
            catch { _doc = new XDocument(new XElement("log")); }
        }
        if (_doc.Root == null)
            _doc.Add(new XElement("log"));
    }

    public Task SaveAsync() => _fs.WriteAllBytesAsync(_path, XmlHelpers.ToBytes(_doc));

    public string? Get(string key) => (string?)_doc.Root?.Element("item_" + key)?.Attribute("eng");

    /// <summary>Sets the same text for every language (as the Unity Project Manager does).</summary>
    public void Set(string key, string text)
    {
        var root = _doc.Root!;
        var node = root.Element("item_" + key);
        if (node == null)
        {
            node = new XElement("item_" + key);
            root.Add(node);
        }
        foreach (var lang in Languages)
            node.SetAttributeValue(lang, text);
    }

    public void Remove(string key) => _doc.Root?.Element("item_" + key)?.Remove();

    public void Rename(string oldKey, string newKey)
    {
        if (oldKey == newKey)
            return;
        var node = _doc.Root?.Element("item_" + oldKey);
        if (node == null)
            return;
        _doc.Root!.Element("item_" + newKey)?.Remove();
        node.Name = "item_" + newKey;
    }
}
