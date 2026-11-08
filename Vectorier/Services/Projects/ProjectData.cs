using System;
using System.Collections.Generic;
using System.Linq;

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
