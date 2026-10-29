using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Vectorier.Services.IO;
using Vectorier.Services.Xml;

namespace Vectorier.Services.Projects;

public enum SubjectType
{
    None,
    Track,
    Location
}

/// <summary>Unlock conditions shared by locations and tracks (List_Payed.xml).</summary>
public sealed class UnlockData
{
    public int UnlockPrice { get; set; }
    public int StarsRequired { get; set; }
    public SubjectType Subject { get; set; }
    public string SubjectName { get; set; } = string.Empty;

    public UnlockData Clone() => (UnlockData)MemberwiseClone();
}

/// <summary>A &lt;Track&gt; (level) entry for one mode.</summary>
public sealed class TrackData
{
    public string Name { get; set; } = string.Empty;
    public UnlockData Unlock { get; set; } = new();
    public string VideoStart { get; set; } = "None";
    public string VideoEnd { get; set; } = "None";
    public List<string> Tricks { get; set; } = new();
    public string StarsTemplate { get; set; } = string.Empty;
    public string RewardTemplate { get; set; } = string.Empty;

    public TrackData Clone() => new()
    {
        Name = Name,
        Unlock = Unlock.Clone(),
        VideoStart = VideoStart,
        VideoEnd = VideoEnd,
        Tricks = Tricks.ToList(),
        StarsTemplate = StarsTemplate,
        RewardTemplate = RewardTemplate,
    };
}

/// <summary>A level: the common-mode track plus its hunter-mode counterpart (NAME_HUNTER).</summary>
public sealed class LevelData
{
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Mode { get; set; } = LocationListService.StoryMode;
    public TrackData Common { get; set; } = new();
    public TrackData Hunter { get; set; } = new();
}

public sealed class LocationData
{
    public string Name { get; set; } = string.Empty;
    public UnlockData Common { get; set; } = new();
    public UnlockData Hunter { get; set; } = new();

    /// <summary>Levels per mode group ("STORY", "BONUS", ...), in order.</summary>
    public Dictionary<string, List<LevelData>> Groups { get; } = new(StringComparer.Ordinal);

    public List<LevelData> GetGroup(string mode)
    {
        if (!Groups.TryGetValue(mode, out var list))
        {
            list = new List<LevelData>();
            Groups[mode] = list;
        }
        return list;
    }

    public IEnumerable<LevelData> AllLevels => Groups.Values.SelectMany(g => g);
}

public sealed class StarsTemplate
{
    public string Name { get; set; } = "New_Stars_Template";
    public bool Completed { get; set; } = true;
    public bool Bonuses { get; set; } = true;
    public bool Tricks { get; set; } = true;
}

public sealed class RewardTemplate
{
    public string Name { get; set; } = "New_Reward_Template";
    public int Coins1 { get; set; } = 100;
    public int Coins2 { get; set; } = 150;
    public int Coins3 { get; set; } = 250;
}

/// <summary>
/// Reads and writes commons/List_Payed.xml (locations, their STORY/BONUS groups and tracks, plus the
/// stars/reward templates). Hunter-mode data lives in the parallel NAME_HUNTER locations and tracks.
/// Everything the editor does not model (CoinSettings, other templates, unknown children) is preserved.
/// </summary>
public sealed class LocationListService
{
    public const string StoryMode = "STORY";
    public const string BonusMode = "BONUS";
    public const string HunterSuffix = "_HUNTER";

    private readonly IFileSystem _fs;
    private readonly string _path;
    private XDocument _doc = new(new XElement("LocationList", new XElement("Locations")));

    public LocationListService(IFileSystem fs, ProjectInfo project)
    {
        _fs = fs;
        _path = VPath.Combine(project.Path, "commons", "List_Payed.xml");
    }

    public List<LocationData> Locations { get; } = new();
    public List<StarsTemplate> StarsTemplates { get; } = new();
    public List<RewardTemplate> RewardTemplates { get; } = new();

    public IEnumerable<LevelData> AllLevels => Locations.SelectMany(l => l.AllLevels);

    public async Task LoadAsync()
    {
        Locations.Clear();
        StarsTemplates.Clear();
        RewardTemplates.Clear();

        if (await _fs.FileExistsAsync(_path))
        {
            try { _doc = XmlHelpers.ParseDocument(await _fs.ReadAllBytesAsync(_path)); }
            catch { _doc = new XDocument(new XElement("LocationList", new XElement("Locations"))); }
        }

        var root = _doc.Root!;
        var locations = root.Element("Locations");
        if (locations != null)
        {
            var hunterNodes = new Dictionary<string, XElement>(StringComparer.Ordinal);
            foreach (var node in locations.Elements("Location"))
            {
                string name = (string?)node.Attribute("Name") ?? string.Empty;
                if (name.EndsWith(HunterSuffix, StringComparison.Ordinal))
                {
                    hunterNodes[name[..^HunterSuffix.Length]] = node;
                    continue;
                }

                var location = new LocationData { Name = name, Common = ReadUnlock(node) };
                foreach (var group in node.Element("Groups")?.Elements("Group") ?? Enumerable.Empty<XElement>())
                {
                    string mode = (string?)group.Attribute("Name") ?? StoryMode;
                    var list = location.GetGroup(mode);
                    foreach (var track in group.Elements("Track"))
                    {
                        var data = ReadTrack(track);
                        list.Add(new LevelData { Name = data.Name, Location = name, Mode = mode, Common = data, Hunter = new TrackData { Name = data.Name + HunterSuffix } });
                    }
                }
                Locations.Add(location);
            }

            foreach (var location in Locations)
            {
                if (!hunterNodes.TryGetValue(location.Name, out var hunterNode))
                    continue;
                location.Hunter = ReadUnlock(hunterNode);
                foreach (var group in hunterNode.Element("Groups")?.Elements("Group") ?? Enumerable.Empty<XElement>())
                {
                    string mode = (string?)group.Attribute("Name") ?? StoryMode;
                    var levels = location.GetGroup(mode);
                    foreach (var track in group.Elements("Track"))
                    {
                        var data = ReadTrack(track);
                        string baseName = data.Name.EndsWith(HunterSuffix, StringComparison.Ordinal) ? data.Name[..^HunterSuffix.Length] : data.Name;
                        var level = levels.FirstOrDefault(l => l.Name == baseName);
                        if (level != null)
                            level.Hunter = data;
                    }
                }
            }
        }

        var templates = root.Element("Templates");
        if (templates != null)
        {
            foreach (var s in templates.Elements("Stars"))
            {
                StarsTemplates.Add(new StarsTemplate
                {
                    Name = (string?)s.Attribute("Name") ?? "Stars_Unknown",
                    Completed = s.Element("Completed") != null,
                    Bonuses = s.Element("Bonuses") != null,
                    Tricks = s.Element("Tricks") != null,
                });
            }
            foreach (var r in templates.Elements("Reward"))
            {
                var reward = new RewardTemplate { Name = (string?)r.Attribute("Name") ?? "Reward_Unknown" };
                foreach (var stars in r.Elements("Stars"))
                {
                    int count = ParseInt((string?)stars.Attribute("Count"));
                    int coins = ParseInt((string?)stars.Attribute("Coins"));
                    if (count == 1) reward.Coins1 = coins;
                    if (count == 2) reward.Coins2 = coins;
                    if (count == 3) reward.Coins3 = coins;
                }
                RewardTemplates.Add(reward);
            }
        }
    }

    public async Task SaveAsync()
    {
        var root = _doc.Root ?? new XElement("LocationList");
        if (_doc.Root == null)
            _doc.Add(root);

        // ---- Templates (other template kinds are preserved) ----
        var templates = root.Element("Templates");
        if (templates == null)
        {
            templates = new XElement("Templates");
            var locationsNode = root.Element("Locations");
            if (locationsNode != null) locationsNode.AddBeforeSelf(templates);
            else root.Add(templates);
        }
        var otherTemplates = templates.Elements().Where(e => e.Name != "Stars" && e.Name != "Reward").ToList();
        templates.RemoveNodes();
        foreach (var t in StarsTemplates)
        {
            var node = new XElement("Stars", new XAttribute("Name", t.Name));
            if (t.Completed) node.Add(new XElement("Completed"));
            if (t.Bonuses) node.Add(new XElement("Bonuses"));
            if (t.Tricks) node.Add(new XElement("Tricks"));
            templates.Add(node);
        }
        foreach (var t in RewardTemplates)
        {
            templates.Add(new XElement("Reward", new XAttribute("Name", t.Name),
                new XElement("Stars", new XAttribute("Count", 1), new XAttribute("Coins", t.Coins1)),
                new XElement("Stars", new XAttribute("Count", 2), new XAttribute("Coins", t.Coins2)),
                new XElement("Stars", new XAttribute("Count", 3), new XAttribute("Coins", t.Coins3))));
        }
        templates.Add(otherTemplates);

        // ---- Locations: keep unknown children per location ----
        var locations = root.Element("Locations");
        if (locations == null)
        {
            locations = new XElement("Locations");
            root.Add(locations);
        }

        var preserved = new Dictionary<string, List<XElement>>(StringComparer.Ordinal);
        var preservedGroupChildren = new Dictionary<string, List<XElement>>(StringComparer.Ordinal);
        foreach (var loc in locations.Elements("Location"))
        {
            string name = (string?)loc.Attribute("Name") ?? string.Empty;
            preserved[name] = loc.Elements().Where(e => e.Name != "Conditions" && e.Name != "Groups").Select(e => new XElement(e)).ToList();
            foreach (var group in loc.Element("Groups")?.Elements("Group") ?? Enumerable.Empty<XElement>())
            {
                string key = name + "|" + (string?)group.Attribute("Name");
                preservedGroupChildren[key] = group.Elements().Where(e => e.Name != "Track").Select(e => new XElement(e)).ToList();
            }
        }

        locations.RemoveNodes();
        foreach (var location in Locations)
            locations.Add(WriteLocation(location, hunter: false, preserved, preservedGroupChildren));
        foreach (var location in Locations)
            locations.Add(WriteLocation(location, hunter: true, preserved, preservedGroupChildren));

        await _fs.WriteAllBytesAsync(_path, XmlHelpers.ToBytes(_doc));
    }

    private static XElement WriteLocation(LocationData location, bool hunter, Dictionary<string, List<XElement>> preserved, Dictionary<string, List<XElement>> preservedGroups)
    {
        string name = hunter ? location.Name + HunterSuffix : location.Name;
        var unlock = hunter ? location.Hunter : location.Common;
        var node = new XElement("Location", new XAttribute("Name", name));

        if (unlock.StarsRequired == 0)
        {
            if (unlock.UnlockPrice > 0)
                node.Add(new XElement("Conditions", new XElement("Payment", new XAttribute("Required", unlock.UnlockPrice))));
        }
        else
        {
            if (unlock.UnlockPrice > 0)
                node.Add(new XAttribute("UnlockPrice", unlock.UnlockPrice));
            node.Add(new XElement("Conditions", WriteStarsCondition(unlock)));
        }

        var groups = new XElement("Groups");
        foreach (var (mode, levels) in OrderedGroups(location))
        {
            var group = new XElement("Group", new XAttribute("Name", mode));
            if (preservedGroups.TryGetValue(name + "|" + mode, out var extra))
                group.Add(extra);
            foreach (var level in levels)
                group.Add(WriteTrack(hunter ? level.Hunter : level.Common, hunter ? level.Name + HunterSuffix : level.Name));
            groups.Add(group);
        }
        node.Add(groups);

        if (preserved.TryGetValue(name, out var others))
            node.Add(others);
        return node;
    }

    private static IEnumerable<(string Mode, List<LevelData> Levels)> OrderedGroups(LocationData location)
    {
        if (location.Groups.TryGetValue(StoryMode, out var story)) yield return (StoryMode, story);
        else yield return (StoryMode, new List<LevelData>());
        if (location.Groups.TryGetValue(BonusMode, out var bonus)) yield return (BonusMode, bonus);
        else yield return (BonusMode, new List<LevelData>());
        foreach (var (mode, levels) in location.Groups)
        {
            if (mode != StoryMode && mode != BonusMode)
                yield return (mode, levels);
        }
    }

    private static XElement WriteStarsCondition(UnlockData unlock)
    {
        var stars = new XElement("Stars", new XAttribute("Required", unlock.StarsRequired));
        if (unlock.Subject != SubjectType.None)
        {
            stars.Add(new XAttribute("Subject", unlock.Subject.ToString()));
            if (!string.IsNullOrEmpty(unlock.SubjectName))
                stars.Add(new XAttribute("Name", unlock.SubjectName));
        }
        return stars;
    }

    /// <summary>Same layout as Unity's CreateTrackNode.</summary>
    private static XElement WriteTrack(TrackData data, string name)
    {
        var track = new XElement("Track", new XAttribute("Name", name));
        var unlock = data.Unlock;
        if (unlock.UnlockPrice > 0) track.Add(new XAttribute("UnlockPrice", unlock.UnlockPrice));
        if (!string.IsNullOrEmpty(data.VideoStart) && data.VideoStart != "None") track.Add(new XAttribute("VideoStart", data.VideoStart));
        if (!string.IsNullOrEmpty(data.VideoEnd) && data.VideoEnd != "None") track.Add(new XAttribute("VideoEnd", data.VideoEnd));

        if (unlock.StarsRequired > 0)
            track.Add(new XElement("Conditions", WriteStarsCondition(unlock)));
        else if (unlock.UnlockPrice > 0)
            track.Add(new XElement("Conditions", new XElement("Payment", new XAttribute("Required", unlock.UnlockPrice))));

        if (data.Tricks.Count > 0)
            track.Add(new XElement("Tricks", data.Tricks.Select(t => new XElement("Trick", new XAttribute("Name", t)))));

        if (!string.IsNullOrEmpty(data.StarsTemplate)) track.Add(new XElement("Stars", new XAttribute("Template", data.StarsTemplate)));
        if (!string.IsNullOrEmpty(data.RewardTemplate)) track.Add(new XElement("Reward", new XAttribute("Template", data.RewardTemplate)));
        return track;
    }

    private static UnlockData ReadUnlock(XElement node)
    {
        var data = new UnlockData();
        if (node.Attribute("UnlockPrice") != null)
            data.UnlockPrice = ParseInt((string?)node.Attribute("UnlockPrice"));

        var conditions = node.Element("Conditions");
        var stars = conditions?.Element("Stars");
        if (stars != null)
        {
            data.StarsRequired = ParseInt((string?)stars.Attribute("Required"));
            if (Enum.TryParse((string?)stars.Attribute("Subject"), out SubjectType subject))
                data.Subject = subject;
            data.SubjectName = (string?)stars.Attribute("Name") ?? string.Empty;
        }
        var payment = conditions?.Element("Payment");
        if (payment != null && data.StarsRequired == 0)
            data.UnlockPrice = ParseInt((string?)payment.Attribute("Required"));
        return data;
    }

    private static TrackData ReadTrack(XElement track)
    {
        var data = new TrackData
        {
            Name = (string?)track.Attribute("Name") ?? string.Empty,
            Unlock = ReadUnlock(track),
            VideoStart = (string?)track.Attribute("VideoStart") ?? "None",
            VideoEnd = (string?)track.Attribute("VideoEnd") ?? "None",
            StarsTemplate = (string?)track.Element("Stars")?.Attribute("Template") ?? string.Empty,
            RewardTemplate = (string?)track.Element("Reward")?.Attribute("Template") ?? string.Empty,
        };
        foreach (var trick in track.Element("Tricks")?.Elements("Trick") ?? Enumerable.Empty<XElement>())
        {
            string? name = (string?)trick.Attribute("Name");
            if (!string.IsNullOrEmpty(name))
                data.Tricks.Add(name);
        }
        return data;
    }

    public static int ParseInt(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return 0;
        string clean = input.Replace(",", "");
        return float.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out float value) ? (int)Math.Round(value) : 0;
    }

    // ================= QUERIES ================= //

    public LocationData? FindLocation(string name) => Locations.FirstOrDefault(l => l.Name == name);

    public LevelData? FindLevel(string name) => AllLevels.FirstOrDefault(l => l.Name == name);

    /// <summary>Level internal name for a position: LOCATION_MODE_NN.</summary>
    public static string MakeLevelName(string location, string mode, int index) => $"{location}_{mode}_{index + 1:D2}";

    public static string MakeDefaultDisplayName(string mode, int locationOrder, int index)
        => $"{(mode == StoryMode ? "Story" : mode == BonusMode ? "Bonus" : mode)} {locationOrder}-{index + 1}";
}

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

    public IEnumerable<string> KeysWithPrefix(string prefix)
        => _doc.Root?.Elements().Select(e => e.Name.LocalName).Where(n => n.StartsWith("item_" + prefix, StringComparison.Ordinal)).Select(n => n[5..])
           ?? Enumerable.Empty<string>();
}

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

    public IEnumerable<string> AllItemNames(string group) => GetItems(group).Select(i => i.Name);
}
