using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier_Editor_Avalonia.Services;
using Vectorier_Editor_Avalonia.Services.Assets;
using Vectorier_Editor_Avalonia.Services.IO;
using Vectorier_Editor_Avalonia.Services.Projects;

namespace Vectorier_Editor_Avalonia.ViewModels.ProjectManager;

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
    public string Header => Mode == LocationListService.StoryMode ? "Story" : Mode == LocationListService.BonusMode ? "Bonus" : Mode;
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
    public string Header => $"{Order}. {Location.Name}";

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

/// <summary>Locations page: collapsible locations whose levels can be added, removed and reordered.</summary>
public sealed partial class LocationsPageViewModel : PmPageViewModel
{
    public override System.Windows.Input.ICommand? DeleteShortcut => DeleteLocationCommand;

    private LevelOperations? _ops;
    private bool _dirty;
    private bool _suppress;

    public LocationsPageViewModel(ProjectManagerViewModel owner) : base(owner, "Locations", "Locations") { }

    private LocationListService List => Owner.LocationList;
    private LocalizationService Localization => Owner.Localization;
    private LevelOperations Ops => _ops ??= new LevelOperations(AppServices.FileSystem, Project, List, Localization);

    public ObservableCollection<LocationItemViewModel> Items { get; } = new();

    [ObservableProperty] private bool _isHunterMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(DeleteLocationCommand), nameof(MoveLocationUpCommand), nameof(MoveLocationDownCommand), nameof(ChangeThumbnailCommand))]
    private LocationItemViewModel? _selectedLocation;

    [ObservableProperty] private LocationLevelItem? _selectedLevel;
    [ObservableProperty] private UnlockEditorViewModel? _unlock;
    [ObservableProperty] private string _editName = string.Empty;
    [ObservableProperty] private string _editDisplayName = string.Empty;
    [ObservableProperty] private Bitmap? _largeThumbnail;

    public bool HasSelection => SelectedLocation != null;

    public void MarkDirty() => _dirty = true;

    public async Task EnsureFreshAsync()
    {
        if (_dirty)
        {
            _dirty = false;
            await ReloadAsync();
        }
    }

    protected override async Task LoadAsync()
    {
        await Owner.EnsureSharedDataAsync();
        Rebuild();
    }

    private void Rebuild()
    {
        string? keepLocation = SelectedLocation?.Name;
        string? keepLevel = SelectedLevel?.Name;
        var expanded = Items.Where(i => !i.IsExpanded).Select(i => i.Name).ToHashSet();

        Items.Clear();
        int order = 1;
        foreach (var location in List.Locations)
        {
            var item = new LocationItemViewModel(location, order++)
            {
                DisplayName = Localization.Get(location.Name) ?? string.Empty,
                IsExpanded = !expanded.Contains(location.Name),
            };
            foreach (var mode in new[] { LocationListService.StoryMode, LocationListService.BonusMode })
            {
                var group = new LocationGroupItem(mode);
                foreach (var level in location.GetGroup(mode))
                    group.Levels.Add(new LocationLevelItem(item, level, Localization.Get(level.Name) ?? string.Empty));
                item.Groups.Add(group);
            }
            foreach (var (mode, levels) in location.Groups)
            {
                if (mode is LocationListService.StoryMode or LocationListService.BonusMode)
                    continue;
                var group = new LocationGroupItem(mode);
                foreach (var level in levels)
                    group.Levels.Add(new LocationLevelItem(item, level, Localization.Get(level.Name) ?? string.Empty));
                item.Groups.Add(group);
            }
            Items.Add(item);
            _ = LoadThumbnailAsync(item, 160);
        }

        SelectedLocation = Items.FirstOrDefault(i => i.Name == keepLocation);
        SelectedLevel = keepLevel == null ? null
            : Items.SelectMany(i => i.Groups).SelectMany(g => g.Levels).FirstOrDefault(l => l.Name == keepLevel);
    }

    private async Task LoadThumbnailAsync(LocationItemViewModel item, int width)
    {
        string? path = await Ops.FindThumbnailAsync(Ops.LocationsIconFolder, item.Name);
        var bitmap = path == null ? null : await ImageLoader.LoadAsync(AppServices.FileSystem, path, width);
        if (width <= 200)
            item.Thumbnail = bitmap;
        else if (item == SelectedLocation)
            LargeThumbnail = bitmap;
    }

    partial void OnSelectedLevelChanged(LocationLevelItem? value)
    {
        if (value != null && value.Owner != SelectedLocation)
            SelectedLocation = value.Owner;
    }

    async partial void OnSelectedLocationChanged(LocationItemViewModel? value)
    {
        _suppress = true;
        EditName = value?.Name ?? string.Empty;
        EditDisplayName = value?.DisplayName ?? string.Empty;
        _suppress = false;
        BuildUnlockEditor();
        LargeThumbnail = null;
        if (value != null)
            await LoadThumbnailAsync(value, 665);
    }

    partial void OnIsHunterModeChanged(bool value) => BuildUnlockEditor();

    private void BuildUnlockEditor()
    {
        var item = SelectedLocation;
        Unlock = item == null ? null : new UnlockEditorViewModel(IsHunterMode ? item.Location.Hunter : item.Location.Common, SaveSoon,
            () => List.AllLevels.Select(l => l.Name),
            () => List.Locations.Where(l => l != item.Location).Select(l => l.Name),
            (subject, name) => StarLimits.Max(List, subject, name));
    }

    private bool _saveQueued;

    private async void SaveSoon()
    {
        if (_saveQueued)
            return;
        _saveQueued = true;
        await Task.Delay(300);
        _saveQueued = false;
        try { await List.SaveAsync(); }
        catch (Exception ex) { await ShowErrorAsync("Save", ex); }
    }

    private async Task AfterStructureChangeAsync()
    {
        await Ops.SaveAllAsync();
        Rebuild();
        Owner.Levels.RebuildItems();
    }

    private async Task RunAsync(string title, Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { await ShowErrorAsync(title, ex); }
    }

    // ================= LOCATIONS ================= //

    [RelayCommand]
    private async Task AddLocationAsync()
    {
        string name = "NEW_LOCATION";
        for (int i = 1; List.FindLocation(name) != null; i++)
            name = $"NEW_LOCATION_{i}";

        var location = new LocationData { Name = name };
        location.GetGroup(LocationListService.StoryMode);
        location.GetGroup(LocationListService.BonusMode);
        List.Locations.Add(location);
        await RunAsync("New Location", AfterStructureChangeAsync);
        SelectedLocation = Items.FirstOrDefault(i => i.Name == name);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteLocationAsync()
    {
        var item = SelectedLocation;
        if (item == null)
            return;
        int count = item.Location.AllLevels.Count();
        if (!await AppServices.Dialogs.ConfirmAsync("Delete Location",
                $"Delete '{item.Name}'{(count > 0 ? $" and its {count} level(s), including their XML files and thumbnails" : "")}?\nThis cannot be undone.",
                "Delete", "Cancel", MessageKind.Warning))
            return;

        await RunAsync("Delete Location", async () =>
        {
            await Ops.DeleteLocationAsync(item.Location);
            SelectedLocation = null;
            await AfterStructureChangeAsync();
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task MoveLocationUpAsync() => MoveLocationAsync(-1);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task MoveLocationDownAsync() => MoveLocationAsync(1);

    private async Task MoveLocationAsync(int direction)
    {
        var item = SelectedLocation;
        if (item == null)
            return;
        int index = List.Locations.IndexOf(item.Location);
        int target = index + direction;
        if (index < 0 || target < 0 || target >= List.Locations.Count)
            return;
        List.Locations.RemoveAt(index);
        List.Locations.Insert(target, item.Location);
        await RunAsync("Reorder Location", AfterStructureChangeAsync);
    }

    [RelayCommand]
    public async Task CommitNameAsync()
    {
        var item = SelectedLocation;
        if (item == null || _suppress)
            return;
        string formatted = LevelOperations.FormatLocationName(EditName);
        if (formatted == item.Name)
        {
            EditName = item.Name;
            return;
        }
        if (List.FindLocation(formatted) != null)
        {
            await AppServices.Dialogs.ShowMessageAsync("Rename Location", $"A location named '{formatted}' already exists.", MessageKind.Warning);
            EditName = item.Name;
            return;
        }
        await RunAsync("Rename Location", async () =>
        {
            await Ops.RenameLocationAsync(item.Location, formatted);
            await AfterStructureChangeAsync();
            SelectedLocation = Items.FirstOrDefault(i => i.Name == formatted);
        });
    }

    [RelayCommand]
    public async Task CommitDisplayNameAsync()
    {
        var item = SelectedLocation;
        if (item == null || _suppress || EditDisplayName == item.DisplayName)
            return;
        item.DisplayName = EditDisplayName;
        Localization.Set(item.Name, EditDisplayName);
        await Localization.SaveAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ChangeThumbnailAsync()
    {
        var item = SelectedLocation;
        var topLevel = AppServices.Shell.CurrentTopLevel;
        if (item == null || topLevel == null)
            return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Location Image (1329 × 512 recommended)",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg" } } },
        });
        if (files.Count == 0)
            return;
        await RunAsync("Thumbnail", async () =>
        {
            await Ops.DeleteImagesAsync(Ops.LocationsIconFolder, item.Name);
            string ext = VPath.GetExtension(files[0].Name).ToLowerInvariant();
            if (ext is not (".png" or ".jpg" or ".jpeg")) ext = ".png";
            await StorageImport.CopyFileAsync(AppServices.FileSystem, files[0], VPath.Combine(Ops.LocationsIconFolder, item.Name + ext));
            await LoadThumbnailAsync(item, 160);
            await LoadThumbnailAsync(item, 665);
        });
    }

    // ================= LEVELS INSIDE LOCATIONS ================= //

    /// <summary>Entries for the "+" menu of a group: a new level, then levels from other locations.</summary>
    public ObservableCollection<LevelPickEntry> GetAddEntries(LocationItemViewModel location, LocationGroupItem group)
    {
        var list = new ObservableCollection<LevelPickEntry> { new("New Level", null, group.Mode) };
        foreach (var other in List.Locations)
        {
            foreach (var level in other.AllLevels)
            {
                if (other == location.Location && level.Mode == group.Mode)
                    continue;
                list.Add(new LevelPickEntry($"{level.Name}  ({Localization.Get(level.Name) ?? ""})", level, group.Mode));
            }
        }
        return list;
    }

    public async Task AddToGroupAsync(LocationItemViewModel location, LocationGroupItem group, LevelPickEntry entry)
    {
        await RunAsync("Add Level", async () =>
        {
            string name;
            if (entry.Level == null)
            {
                name = Ops.CreateLevel(location.Location, group.Mode).Name;
            }
            else
            {
                await Ops.MoveToLocationAsync(entry.Level, location.Location, group.Mode);
                name = entry.Level.Name;
            }
            await AfterStructureChangeAsync();
            SelectedLevel = Items.SelectMany(i => i.Groups).SelectMany(g => g.Levels).FirstOrDefault(l => l.Name == name);
        });
    }

    public async Task RemoveFromGroupAsync(LocationLevelItem level)
    {
        if (!await AppServices.Dialogs.ConfirmAsync("Remove Level",
                $"Remove '{level.Name}' from {level.Owner.Name}?\nA level always belongs to a location, so it is deleted together with its XML file and thumbnail.\n\nTo keep it, add it to another location with that location's '+' button instead.",
                "Delete Level", "Cancel", MessageKind.Warning))
            return;

        await RunAsync("Remove Level", async () =>
        {
            await Ops.DeleteLevelAsync(level.Level);
            SelectedLevel = null;
            await AfterStructureChangeAsync();
        });
    }

    public async Task MoveLevelAsync(LocationLevelItem level, int direction)
    {
        string? name = null;
        await RunAsync("Reorder Level", async () =>
        {
            await Ops.MoveWithinGroupAsync(level.Level, direction);
            name = level.Level.Name;
            await AfterStructureChangeAsync();
        });
        SelectedLevel = Items.SelectMany(i => i.Groups).SelectMany(g => g.Levels).FirstOrDefault(l => l.Name == name);
    }

    public void OpenLevel(LocationLevelItem level) => Owner.OpenLevel(level.Level);
}
