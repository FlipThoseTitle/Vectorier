using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Vectorier.Services.IO;
using Vectorier.Services.Xml;

namespace Vectorier.Services.Projects;

public sealed class ShopItem
{
    public string Name { get; set; } = string.Empty;
    public int Price { get; set; }
    public string ShopImage { get; set; } = string.Empty;
    public string TrackImage { get; set; } = string.Empty;
}

/// <summary>commons/Shop_payed.xml: TRICK and CLOTHING (gear) groups. Other groups are preserved.</summary>
public sealed class ShopService
{
    public const string TrickGroup = "TRICK";
    public const string ClothingGroup = "CLOTHING";

    private readonly IFileSystem _fs;
    private readonly string _path;
    private XDocument _doc = new(new XElement("Shop"));

    public ShopService(IFileSystem fs, ProjectInfo project)
    {
        _fs = fs;
        _path = VPath.Combine(project.Path, "commons", "Shop_payed.xml");
    }

    public async Task LoadAsync()
    {
        if (await _fs.FileExistsAsync(_path))
        {
            try { _doc = XmlHelpers.ParseDocument(await _fs.ReadAllBytesAsync(_path)); }
            catch { _doc = new XDocument(new XElement("Shop")); }
        }
        if (_doc.Root == null)
            _doc.Add(new XElement("Shop"));
    }

    public List<ShopItem> GetItems(string group)
    {
        var node = _doc.Root!.Elements("Group").FirstOrDefault(g => (string?)g.Attribute("Name") == group);
        return node?.Elements("Item").Select(i => new ShopItem
        {
            Name = (string?)i.Attribute("Name") ?? string.Empty,
            Price = LocationListService.ParseInt((string?)i.Attribute("Price")),
            ShopImage = (string?)i.Attribute("ShopImage") ?? string.Empty,
            TrackImage = (string?)i.Attribute("TrackImage") ?? string.Empty,
        }).ToList() ?? new List<ShopItem>();
    }

    public void SetItems(string group, IEnumerable<ShopItem> items, bool includeTrackImage)
    {
        var root = _doc.Root!;
        var node = root.Elements("Group").FirstOrDefault(g => (string?)g.Attribute("Name") == group);
        if (node == null)
        {
            node = new XElement("Group", new XAttribute("Name", group));
            root.Add(node);
        }
        var others = node.Elements().Where(e => e.Name != "Item").ToList();
        node.RemoveNodes();
        node.Add(others);
        foreach (var item in items)
        {
            var x = new XElement("Item",
                new XAttribute("Price", item.Price),
                new XAttribute("Name", item.Name),
                new XAttribute("ShopImage", item.ShopImage));
            if (includeTrackImage)
                x.Add(new XAttribute("TrackImage", item.TrackImage));
            node.Add(x);
        }
    }

    public Task SaveAsync() => _fs.WriteAllBytesAsync(_path, XmlHelpers.ToBytes(_doc));
}
