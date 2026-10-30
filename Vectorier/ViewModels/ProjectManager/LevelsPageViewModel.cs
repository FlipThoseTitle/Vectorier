using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Services;
using Vectorier.Services.Assets;
using Vectorier.Services.IO;
using Vectorier.Services.Projects;
using Vectorier.Services.Localization;

namespace Vectorier.ViewModels.ProjectManager;

public sealed partial class LevelItemViewModel : ObservableObject
{
    public LevelItemViewModel(LevelData level)
    {
        Level = level;
    }

    public LevelData Level { get; }

    public string Name => Level.Name;
    public string Location => Level.Location;

    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private Bitmap? _thumbnail;
    [ObservableProperty] private bool _hasXml;

    public void RaiseNameChanged()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Location));
    }
}

/// <summary>Levels page: global list of levels per mode, with their properties.</summary>
public sealed partial class LevelsPageViewModel : PmPageViewModel
{
    public override System.Windows.Input.ICommand? DeleteShortcut => DeleteLevelCommand;

    public static string AllLocations => Loc.T("All Locations");

    private LevelOperations? _ops;
    private bool _suppress;
    private readonly List<LevelItemViewModel> _allItems = new();

    public LevelsPageViewModel(ProjectManagerViewModel owner) : base(owner, Loc.T("Levels"), "Levels")
    {
        // "All Locations" is always present, so the filter box never starts blank.
        LocationFilters.Add(AllLocations);
    }

    private List<TrickInfo> _tricks = new();

    public override bool HasSearch => true;

    private LocationListService List => Owner.LocationList;
    private LocalizationService Localization => Owner.Localization;
    private LevelOperations Ops => _ops ??= new LevelOperations(AppServices.FileSystem, Project, List, Localization);

    public ObservableCollection<LevelItemViewModel> Items { get; } = new();
    public ObservableCollection<string> LocationFilters { get; } = new();
    public ObservableCollection<string> LocationNames { get; } = new();

    [ObservableProperty] private bool _isStoryMode = true;
    [ObservableProperty] private bool _isHunterMode;
    [ObservableProperty] private string? _selectedLocationFilter = AllLocations;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(DeleteLevelCommand), nameof(DuplicateLevelCommand), nameof(MoveUpCommand), nameof(MoveDownCommand),
        nameof(OpenInEditorCommand), nameof(BrowseXmlCommand), nameof(ChangeThumbnailCommand), nameof(ClearXmlCommand))]
    private LevelItemViewModel? _selectedItem;

    [ObservableProperty] private TrackEditorViewModel? _track;
    [ObservableProperty] private string _editDisplayName = string.Empty;
    [ObservableProperty] private string? _editLocation;
    [ObservableProperty] private Bitmap? _largeThumbnail;
    [ObservableProperty] private string _xmlPathText = string.Empty;
    [ObservableProperty] private string _countText = string.Empty;

    public bool HasSelection => SelectedItem != null;
    public string Mode => IsStoryMode ? LocationListService.StoryMode : LocationListService.BonusMode;
    public string ModeLabel => IsStoryMode ? Loc.T("Story") : Loc.T("Bonus");

    partial void OnIsStoryModeChanged(bool value)
    {
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(ModeLabel));
        RebuildItems();
    }

    partial void OnIsHunterModeChanged(bool value) => BuildTrackEditor();

    partial void OnSelectedLocationFilterChanged(string? value)
    {
        if (value == null && !_suppress)
        {
            // The ComboBox clears its selection while its items change; fall back to all locations.
            SelectedLocationFilter = AllLocations;
            return;
        }
        if (!_suppress)
            ApplyFilter();
    }

    async partial void OnSelectedItemChanged(LevelItemViewModel? value)
    {
        _suppress = true;
        EditDisplayName = value?.DisplayName ?? string.Empty;
        EditLocation = value?.Location;
        _suppress = false;
        BuildTrackEditor();
        await RefreshSelectionDetailsAsync();
    }

    protected override async Task LoadAsync()
    {
        await Owner.EnsureSharedDataAsync();
        _tricks = TrickInfo.Load(Project, Owner.Shop, Localization);
        RebuildItems();
    }

    public void RebuildItems()
    {
        string? keep = SelectedItem?.Name;
        _suppress = true;

        string? filter = SelectedLocationFilter;
        var names = List.Locations.Select(l => l.Name).ToList();
        if (!LocationNames.SequenceEqual(names))
        {
            // Keep "All Locations" (index 0) in place so the ComboBox never loses its item.
            while (LocationFilters.Count > 1)
                LocationFilters.RemoveAt(LocationFilters.Count - 1);
            LocationNames.Clear();
            foreach (var name in names)
            {
                LocationFilters.Add(name);
                LocationNames.Add(name);
            }
        }
        string target = filter != null && LocationFilters.Contains(filter) ? filter : AllLocations;
        // Re-assign through null so the ComboBox shows it even when the value didn't change.
        SelectedLocationFilter = null;
        SelectedLocationFilter = target;

        _allItems.Clear();
        foreach (var location in List.Locations)
        {
            if (!location.Groups.TryGetValue(Mode, out var group))
                continue;
            foreach (var level in group)
            {
                var item = new LevelItemViewModel(level)
                {
                    DisplayName = Localization.Get(level.Name) ?? string.Empty,
                };
                _allItems.Add(item);
                _ = LoadItemThumbnailAsync(item);
            }
        }

        _suppress = false;
        ApplyFilter();
        SelectedItem = keep == null ? null : Items.FirstOrDefault(i => i.Name == keep);
    }

    protected override void ApplyFilter()
    {
        var selected = SelectedItem;
        Items.Clear();
        foreach (var item in _allItems)
        {
            if (SelectedLocationFilter != null && SelectedLocationFilter != AllLocations && item.Location != SelectedLocationFilter)
                continue;
            if (!Matches(item.Name) && !Matches(item.DisplayName))
                continue;
            Items.Add(item);
        }
        CountText = IsStoryMode ? Loc.F("Story levels: {0}", Items.Count) : Loc.F("Bonus levels: {0}", Items.Count);
        if (selected != null && Items.Contains(selected))
            SelectedItem = selected;
    }

    public void SelectLevelByName(string name)
    {
        var level = List.FindLevel(name);
        if (level == null)
            return;
        IsStoryMode = level.Mode != LocationListService.BonusMode;
        SelectedLocationFilter = AllLocations;
        SearchText = string.Empty;
        SelectedItem = Items.FirstOrDefault(i => i.Name == name);
    }

    private async Task LoadItemThumbnailAsync(LevelItemViewModel item)
    {
        string? path = await Ops.FindThumbnailAsync(Ops.StoriesIconFolder, item.Name);
        item.Thumbnail = path == null ? null : await ImageLoader.LoadAsync(AppServices.FileSystem, path, 128);
        item.HasXml = await AppServices.FileSystem.FileExistsAsync(Ops.XmlPathFor(item.Name));
    }

    private async Task RefreshSelectionDetailsAsync()
    {
        var item = SelectedItem;
        if (item == null)
        {
            LargeThumbnail = null;
            XmlPathText = string.Empty;
            return;
        }

        string? path = await Ops.FindThumbnailAsync(Ops.StoriesIconFolder, item.Name);
        LargeThumbnail = path == null ? null : await ImageLoader.LoadAsync(AppServices.FileSystem, path, 512);
        string xml = Ops.XmlPathFor(item.Name);
        item.HasXml = await AppServices.FileSystem.FileExistsAsync(xml);
        XmlPathText = item.HasXml ? xml : string.Empty;
    }

    private void BuildTrackEditor()
    {
        var item = SelectedItem;
        if (item == null)
        {
            Track = null;
            return;
        }

        var data = IsHunterMode ? item.Level.Hunter : item.Level.Common;
        Track = new TrackEditorViewModel(data, SaveSoon,
            () => List.AllLevels.Select(l => l.Name),
            () => List.Locations.Select(l => l.Name),
            _tricks,
            List.StarsTemplates.Select(t => t.Name).ToList(),
            List.RewardTemplates.Select(t => t.Name).ToList(),
            GetVideos(),
            (subject, name) => StarLimits.Max(List, subject, name));
    }

    private List<string> _videos = new() { "None" };

    private List<string> GetVideos()
    {
        _ = RefreshVideosAsync();
        return _videos;
    }

    private async Task RefreshVideosAsync()
    {
        var files = await AppServices.FileSystem.GetFilesAsync(VPath.Combine(Project.Path, "videos"));
        var list = new List<string> { "None" };
        list.AddRange(files.Where(f => VPath.HasExtension(f, ".mp4")).Select(VPath.GetFileName));
        if (!list.SequenceEqual(_videos))
        {
            _videos = list;
            BuildTrackEditor();
        }
    }

    // ================= SAVING ================= //

    private bool _saveQueued;

    /// <summary>Changes are written right away (debounced), as the Unity Project Manager does.</summary>
    private async void SaveSoon()
    {
        if (_saveQueued)
            return;
        _saveQueued = true;
        await Task.Delay(300);
        _saveQueued = false;
        try
        {
            await Ops.SaveAllAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(Loc.T("Save"), ex);
        }
    }

    [RelayCommand]
    public async Task CommitDisplayNameAsync()
    {
        var item = SelectedItem;
        if (item == null || _suppress || EditDisplayName == item.DisplayName)
            return;
        item.DisplayName = EditDisplayName;
        Localization.Set(item.Name, EditDisplayName);
        await Localization.SaveAsync();
    }

    async partial void OnEditLocationChanged(string? value)
    {
        var item = SelectedItem;
        if (_suppress || item == null || value == null || value == item.Location)
            return;
        var target = List.FindLocation(value);
        if (target == null)
            return;

        await RunAsync(Loc.T("Move Level"), async () =>
        {
            await Ops.MoveToLocationAsync(item.Level, target, item.Level.Mode);
            await Ops.SaveAllAsync();
            string name = item.Level.Name;
            RebuildItems();
            SelectedItem = Items.FirstOrDefault(i => i.Name == name);
            Owner.Locations.MarkDirty();
        });
    }

    // ================= COMMANDS ================= //

    private LocationData? ResolveTargetLocation()
    {
        if (SelectedLocationFilter != null && SelectedLocationFilter != AllLocations)
            return List.FindLocation(SelectedLocationFilter);
        if (SelectedItem != null)
            return List.FindLocation(SelectedItem.Location);
        return List.Locations.FirstOrDefault();
    }

    private async Task<LocationData?> EnsureLocationAsync()
    {
        var location = ResolveTargetLocation();
        if (location != null)
            return location;

        if (!await AppServices.Dialogs.ConfirmAsync(Loc.T("New Level"), Loc.T("Levels belong to a location, and this project has none yet.\n\nCreate a location now?"), Loc.T("Create Location")))
            return null;

        location = new LocationData { Name = "NEW_LOCATION" };
        List.Locations.Add(location);
        Owner.Locations.MarkDirty();
        return location;
    }

    [RelayCommand]
    private async Task AddLevelAsync()
    {
        await RunAsync(Loc.T("New Level"), async () =>
        {
            var location = await EnsureLocationAsync();
            if (location == null)
                return;
            var level = Ops.CreateLevel(location, Mode);
            await Ops.SaveAllAsync();
            RebuildItems();
            SelectedItem = Items.FirstOrDefault(i => i.Name == level.Name);
            Owner.Locations.MarkDirty();
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteLevelAsync()
    {
        var item = SelectedItem;
        if (item == null)
            return;
        if (!await AppServices.Dialogs.ConfirmAsync(Loc.T("Delete Level"),
                Loc.F("Delete '{0}'?\nIts XML file and thumbnail are deleted too, and the following levels are renumbered.", item.Name),
                Loc.T("Delete"), Loc.T("Cancel"), MessageKind.Warning))
            return;

        await RunAsync(Loc.T("Delete Level"), async () =>
        {
            await Ops.DeleteLevelAsync(item.Level);
            await Ops.SaveAllAsync();
            SelectedItem = null;
            RebuildItems();
            Owner.Locations.MarkDirty();
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DuplicateLevelAsync()
    {
        var item = SelectedItem;
        if (item == null)
            return;
        await RunAsync(Loc.T("Duplicate Level"), async () =>
        {
            var copy = await Ops.DuplicateLevelAsync(item.Level);
            await Ops.SaveAllAsync();
            RebuildItems();
            SelectedItem = Items.FirstOrDefault(i => i.Name == copy.Name);
            Owner.Locations.MarkDirty();
        });
    }

    [RelayCommand]
    private async Task ImportXmlAsync()
    {
        var topLevel = AppServices.Shell.CurrentTopLevel;
        if (topLevel == null)
            return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("Import Level XML"),
            AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType(Loc.T("Level XML")) { Patterns = new[] { "*.xml" } } },
        });
        if (files.Count == 0)
            return;

        await RunAsync(Loc.T("Import Level"), async () =>
        {
            var location = await EnsureLocationAsync();
            if (location == null)
                return;
            LevelData? last = null;
            foreach (var file in files)
            {
                var level = Ops.CreateLevel(location, Mode);
                await StorageImport.CopyFileAsync(AppServices.FileSystem, file, Ops.XmlPathFor(level.Name));
                last = level;
            }
            await Ops.SaveAllAsync();
            RebuildItems();
            if (last != null)
                SelectedItem = Items.FirstOrDefault(i => i.Name == last.Name);
            Owner.Locations.MarkDirty();
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task MoveUpAsync() => MoveAsync(-1);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task MoveDownAsync() => MoveAsync(1);

    private async Task MoveAsync(int direction)
    {
        var item = SelectedItem;
        if (item == null)
            return;
        await RunAsync(Loc.T("Reorder Level"), async () =>
        {
            await Ops.MoveWithinGroupAsync(item.Level, direction);
            await Ops.SaveAllAsync();
            string name = item.Level.Name;
            RebuildItems();
            SelectedItem = Items.FirstOrDefault(i => i.Name == name);
            Owner.Locations.MarkDirty();
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task BrowseXmlAsync()
    {
        var item = SelectedItem;
        var topLevel = AppServices.Shell.CurrentTopLevel;
        if (item == null || topLevel == null)
            return;

        var start = await AppServices.FileSystem.GetStorageFolderAsync(Project.LevelsFolder, topLevel.StorageProvider);
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.F("Select the XML for {0}", item.Name),
            AllowMultiple = false,
            SuggestedStartLocation = start,
            FileTypeFilter = new[] { new FilePickerFileType(Loc.T("Level XML")) { Patterns = new[] { "*.xml" } } },
        });
        if (files.Count == 0)
            return;

        string target = Ops.XmlPathFor(item.Name);
        string? native = files[0].TryGetLocalPath();
        if (native != null && AppServices.FileSystem.TryGetRelativePath(native) == target)
            return;

        if (await AppServices.FileSystem.FileExistsAsync(target) &&
            !await AppServices.Dialogs.ConfirmAsync(Loc.T("Level XML"), Loc.F("{0}.xml already exists and will be replaced. Continue?", item.Name), Loc.T("Replace"), Loc.T("Cancel"), MessageKind.Warning))
            return;

        await RunAsync(Loc.T("Level XML"), async () =>
        {
            await StorageImport.CopyFileAsync(AppServices.FileSystem, files[0], target);
            await RefreshSelectionDetailsAsync();
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ClearXmlAsync()
    {
        var item = SelectedItem;
        if (item == null || !item.HasXml)
            return;
        if (!await AppServices.Dialogs.ConfirmAsync(Loc.T("Level XML"), Loc.F("Delete {0}.xml?\nThe level entry itself is kept.", item.Name), Loc.T("Delete"), Loc.T("Cancel"), MessageKind.Warning))
            return;
        await AppServices.FileSystem.DeleteFileAsync(Ops.XmlPathFor(item.Name));
        await RefreshSelectionDetailsAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ChangeThumbnailAsync()
    {
        var item = SelectedItem;
        var topLevel = AppServices.Shell.CurrentTopLevel;
        if (item == null || topLevel == null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("Select Level Thumbnail (512 × 340 recommended)"),
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(Loc.T("Images")) { Patterns = new[] { "*.png", "*.jpg", "*.jpeg" } } },
        });
        if (files.Count == 0)
            return;

        await RunAsync(Loc.T("Thumbnail"), async () =>
        {
            await Ops.DeleteImagesAsync(Ops.StoriesIconFolder, item.Name);
            string ext = VPath.GetExtension(files[0].Name).ToLowerInvariant();
            if (ext is not (".png" or ".jpg" or ".jpeg")) ext = ".png";
            await StorageImport.CopyFileAsync(AppServices.FileSystem, files[0], VPath.Combine(Ops.StoriesIconFolder, item.Name + ext));
            await LoadItemThumbnailAsync(item);
            await RefreshSelectionDetailsAsync();
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void OpenInEditor()
    {
        if (SelectedItem != null)
            Owner.OpenLevel(SelectedItem.Level);
    }

    [RelayCommand]
    private async Task EditTemplatesAsync()
    {
        var dialog = new TemplatesDialogViewModel(List);
        if (await AppServices.Dialogs.ShowAsync(dialog) is true)
        {
            dialog.Apply();
            await RunAsync(Loc.T("Templates"), async () =>
            {
                await List.SaveAsync();
                BuildTrackEditor();
            });
        }
    }

    private async Task RunAsync(string title, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(title, ex);
        }
    }
}
