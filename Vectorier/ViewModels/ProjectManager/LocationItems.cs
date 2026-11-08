using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Vectorier.Services.Localization;
using Vectorier.Services.Projects;

namespace Vectorier.ViewModels.ProjectManager;

public sealed partial class LocationLevelItem : ObservableObject
{
    public LocationLevelItem(LocationItemViewModel owner, LevelData level, string displayName)
    {
        Owner = owner;
        Level = level;
        _displayName = displayName;
    }

    public LocationItemViewModel Owner { get; }
    public LevelData Level { get; }
    public string Name => Level.Name;

    [ObservableProperty] private string _displayName;
}

public sealed partial class LocationGroupItem : ObservableObject
{
    public LocationGroupItem(string mode) => Mode = mode;
    public string Mode { get; }
    public string Header => Mode == LocationListService.StoryMode ? Loc.T("Story") : Mode == LocationListService.BonusMode ? Loc.T("Bonus") : Mode;
    public ObservableCollection<LocationLevelItem> Levels { get; } = new();
}

public sealed partial class LocationItemViewModel : ObservableObject
{
    public LocationItemViewModel(LocationData location, int order)
    {
        Location = location;
        Order = order;
    }

    public LocationData Location { get; }
    public int Order { get; }
    public string Name => Location.Name;
    public string Header => Loc.F("{0}. {1}", Order, Location.Name);

    [ObservableProperty] private bool _isExpanded = true;
    [ObservableProperty] private Bitmap? _thumbnail;
    [ObservableProperty] private string _displayName = string.Empty;

    public ObservableCollection<LocationGroupItem> Groups { get; } = new();

    public int LevelCount => Groups.Sum(g => g.Levels.Count);
}

/// <summary>An entry of the "+" menu: a level that can be moved into a location.</summary>
public sealed class LevelPickEntry
{
    public LevelPickEntry(string header, LevelData? level, string? mode)
    {
        Header = header;
        Level = level;
        Mode = mode;
    }

    public string Header { get; }
    public LevelData? Level { get; }
    public string? Mode { get; }
}
