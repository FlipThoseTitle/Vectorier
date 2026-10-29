using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Platform.Storage;

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
        Xml = xml;
    }

    public string Name { get; }
    public string SourceFile { get; }
    public XElement Xml { get; }

    /// <summary>Building entry / exit (InX, InY / OutX, OutY), relative to the instance; null for plain objects.</summary>
    public Point? In => Point2("InX", "InY");
    public Point? Out => Point2("OutX", "OutY");

    /// <summary>A Building definition: it has any of the In / Out / Box attributes.</summary>
    public bool IsBuilding => BuildingAttributes.Any(a => Xml.Attribute(a) != null);

    public static readonly string[] BuildingAttributes = { "InX", "InY", "OutX", "OutY", "BoxX", "BoxY", "BoxWidth", "BoxHeight" };

    private Point? Point2(string x, string y)
    {
        if (Xml.Attribute(x) == null && Xml.Attribute(y) == null)
            return null;
        return new Point(Num.Parse((string?)Xml.Attribute(x)), Num.Parse((string?)Xml.Attribute(y)));
    }

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

/// <summary>A &lt;Model&gt; entry of a level's &lt;Models&gt; block (the characters taking part in the level).</summary>
public sealed class LevelModelDefinition
{
    /// <summary>Attributes in their original order (Name, Type, Color, BirthSpawn, AI, Time, ...).</summary>
    public List<KeyValuePair<string, string>> Attributes { get; set; } = new();

    public string Get(string key) => Attributes.FirstOrDefault(a => a.Key == key).Value ?? string.Empty;

    public void Set(string key, string? value)
    {
        int index = Attributes.FindIndex(a => a.Key == key);
        if (string.IsNullOrEmpty(value))
        {
            if (index >= 0)
                Attributes.RemoveAt(index);
            return;
        }
        if (index >= 0)
            Attributes[index] = new KeyValuePair<string, string>(key, value);
        else
            Attributes.Add(new KeyValuePair<string, string>(key, value));
    }

    public LevelModelDefinition Clone() => new() { Attributes = Attributes.ToList() };
}

/// <summary>Level-wide settings (the Unity ExportConfig): sets, music, models and coins.</summary>
public sealed class LevelSettings
{
    public const string DefaultCommonModels =
        "<Model Name=\"Player\" Type=\"1\" Color=\"0\" BirthSpawn=\"DefaultSpawn\" AI=\"0\" Time=\"0\" Respawns=\"Hunter\" ForceBlasts=\"Hunter\" Trick=\"1\" Item=\"1\" Victory=\"1\" Lose=\"1\"/>\n" +
        "<Model Name=\"Hunter\" Type=\"0\" Color=\"0\" BirthSpawn=\"DefaultSpawn\" AI=\"1\" Time=\"1.5\" AllowedSpawns=\"Respawn\" Skins=\"hunter\" Murders=\"Player\" Arrests=\"Player\" Icon=\"1\"/>";

    public const string DefaultHunterModels =
        "<Model Name=\"Player\" Type=\"0\" Color=\"0\" BirthSpawn=\"DefaultSpawn\" AI=\"5\" Time=\"0\" Victory=\"1\" Respawns=\"Hunter\"/>\n" +
        "<Model Name=\"Hunter\" Type=\"1\" Color=\"0\" BirthSpawn=\"DefaultSpawn\" AI=\"0\" Time=\"1.5\" Trick=\"1\" Item=\"1\" Skins=\"hunter\" Murders=\"Player\" Arrests=\"Player\" Lose=\"1\" AllowedSpawns=\"Despawn\"/>";

    public List<string> CitySets { get; set; } = new() { "buildings.xml" };
    public List<string> GroundSets { get; set; } = new() { "objects.xml" };
    public List<string> LibrarySets { get; set; } = new();

    public string MusicName { get; set; } = "music_dinamic";
    public double MusicVolume { get; set; } = 0.3;

    public List<LevelModelDefinition> CommonModels { get; set; } = ParseModels(DefaultCommonModels);
    public List<LevelModelDefinition> HunterModels { get; set; } = ParseModels(DefaultHunterModels);

    public int CoinValue { get; set; } = 50;

    /// <summary>Unknown children of &lt;Root&gt; (kept verbatim, written before &lt;Track&gt;).</summary>
    public List<XElement> ExtraRootElements { get; set; } = new();

    /// <summary>Layer factors present in the source file, so empty layers survive a round trip.</summary>
    public List<string> SourceFactors { get; set; } = new();

    public static List<LevelModelDefinition> ParseModels(string xml)
    {
        var list = new List<LevelModelDefinition>();
        try
        {
            var root = XElement.Parse("<Models>" + xml + "</Models>");
            foreach (var model in root.Elements("Model"))
            {
                var def = new LevelModelDefinition();
                foreach (var attribute in model.Attributes())
                    def.Attributes.Add(new KeyValuePair<string, string>(attribute.Name.LocalName, attribute.Value));
                list.Add(def);
            }
        }
        catch
        {
            // invalid text: no models
        }
        return list;
    }

    public static string FormatModels(IEnumerable<LevelModelDefinition> models)
    {
        return string.Join("\n", models.Select(m =>
            "<Model " + string.Join(" ", m.Attributes.Select(a => $"{a.Key}=\"{System.Security.SecurityElement.Escape(a.Value)}\"")) + "/>"));
    }

    public LevelSettings Clone() => new()
    {
        CitySets = CitySets.ToList(),
        GroundSets = GroundSets.ToList(),
        LibrarySets = LibrarySets.ToList(),
        MusicName = MusicName,
        MusicVolume = MusicVolume,
        CommonModels = CommonModels.Select(m => m.Clone()).ToList(),
        HunterModels = HunterModels.Select(m => m.Clone()).ToList(),
        CoinValue = CoinValue,
        ExtraRootElements = ExtraRootElements.Select(e => new XElement(e)).ToList(),
        SourceFactors = SourceFactors.ToList(),
    };
}

/// <summary>
/// The working representation of an opened level. The Hierarchy, Scene and Inspector all read and edit
/// this object; XML is only produced when saving.
/// </summary>
public sealed class SceneDocument
{
    /// <summary>Level name without extension, e.g. DOWNTOWN_STORY_02.</summary>
    public string LevelName { get; set; } = "New_Level";

    public LevelSettings Settings { get; set; } = new();

    /// <summary>Top-level elements in hierarchy order. Each carries its own layer.</summary>
    public ObservableCollection<SceneElement> Roots { get; } = new();

    /// <summary>Relative path of the XML inside the Vectorier folder, or null when not saved yet.</summary>
    public string? FilePath { get; set; }

    /// <summary>File outside the Vectorier folder (opened through a picker).</summary>
    public IStorageFile? ExternalFile { get; set; }

    public bool HasLocation => FilePath != null || ExternalFile != null;

    /// <summary>All elements, depth-first, in hierarchy order (inline content only).</summary>
    public IEnumerable<SceneElement> AllElements()
    {
        foreach (var root in Roots)
        {
            foreach (var e in Walk(root))
                yield return e;
        }
    }

    public static IEnumerable<SceneElement> Walk(SceneElement element)
    {
        yield return element;
        if (element is ObjectElement obj)
        {
            foreach (var child in obj.Children)
            {
                foreach (var e in Walk(child))
                    yield return e;
            }
        }
    }

    /// <summary>Siblings list that contains the element (the parent's children, or the roots).</summary>
    public IList<SceneElement> SiblingsOf(SceneElement element)
        => element.Parent != null ? element.Parent.Children : Roots;

    public void Insert(SceneElement element, ObjectElement? parent, int index = -1)
    {
        if (parent != null)
        {
            parent.AddChild(element, index);
            return;
        }

        element.Parent = null;
        if (index < 0 || index > Roots.Count)
            Roots.Add(element);
        else
            Roots.Insert(index, element);
    }

    public void Remove(SceneElement element)
    {
        if (element.Parent != null)
            element.Parent.RemoveChild(element);
        else
            Roots.Remove(element);
    }

    public SceneElement? FindById(int id) => AllElements().FirstOrDefault(e => e.Id == id);

    // ================= UNDO SNAPSHOTS ================= //

    public sealed record Snapshot(List<SceneElement> Roots, LevelSettings Settings);

    public Snapshot CreateSnapshot()
        => new(Roots.Select(ObjectElement.CloneKeepingIds).ToList(), Settings.Clone());

    public void RestoreSnapshot(Snapshot snapshot)
    {
        Roots.Clear();
        foreach (var root in snapshot.Roots)
        {
            var copy = ObjectElement.CloneKeepingIds(root);
            copy.Parent = null;
            Roots.Add(copy);
        }
        Settings = snapshot.Settings.Clone();
    }
}
