using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Vectorier.Models.Scene;

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
    /// <summary>The music's volume: always the game's 0.3 (no longer set per level).</summary>
    public const double FixedMusicVolume = 0.3;

    public double MusicVolume { get; set; } = FixedMusicVolume;

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
