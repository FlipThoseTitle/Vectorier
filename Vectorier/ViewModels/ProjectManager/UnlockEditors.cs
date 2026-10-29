using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Services;
using Vectorier.Services.Assets;
using Vectorier.Services.IO;
using Vectorier.Services.Projects;
using Vectorier.ViewModels.Dialogs;

namespace Vectorier.ViewModels.ProjectManager;

/// <summary>
/// Highest "Stars Required" for an unlock: 3 for a track; 3 × the location's Story + Bonus levels for a location;
/// 3 × every Story + Bonus level of the project when there is no subject.
/// </summary>
public static class StarLimits
{
    public static int Max(LocationListService list, SubjectType subject, string? subjectName)
    {
        static int Count(LocationData location) =>
            (location.Groups.TryGetValue(LocationListService.StoryMode, out var story) ? story.Count : 0) +
            (location.Groups.TryGetValue(LocationListService.BonusMode, out var bonus) ? bonus.Count : 0);

        return subject switch
        {
            SubjectType.Track => 3,
            SubjectType.Location => 3 * (subjectName != null && list.FindLocation(subjectName) is { } loc ? Count(loc) : 0),
            _ => 3 * list.Locations.Sum(Count),
        };
    }
}

/// <summary>Edits unlock conditions (price, stars, subject) and reports every change.</summary>
public sealed partial class UnlockEditorViewModel : ObservableObject
{
    private readonly UnlockData _data;
    private readonly Action _changed;
    private readonly Func<IEnumerable<string>> _tracks;
    private readonly Func<IEnumerable<string>> _locations;
    private readonly Func<SubjectType, string?, int>? _maxStarsFunc;
    private bool _loading;

    public UnlockEditorViewModel(UnlockData data, Action changed, Func<IEnumerable<string>> tracks, Func<IEnumerable<string>> locations,
        Func<SubjectType, string?, int>? maxStars = null)
    {
        _data = data;
        _changed = changed;
        _tracks = tracks;
        _locations = locations;
        _maxStarsFunc = maxStars;

        _loading = true;
        UnlockPrice = Math.Max(0, data.UnlockPrice);
        StarsRequired = Math.Max(0, data.StarsRequired);
        Subject = data.Subject;
        RefreshSubjectNames();
        SubjectName = data.SubjectName;
        _loading = false;
        UpdateLimit();
    }

    public static IReadOnlyList<SubjectType> Subjects { get; } = Enum.GetValues<SubjectType>();

    [ObservableProperty] private int _unlockPrice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSubject))]
    private int _starsRequired;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSubjectName))]
    private SubjectType _subject;

    [ObservableProperty] private string? _subjectName;

    /// <summary>Highest allowed Stars Required for the current subject.</summary>
    [ObservableProperty] private int _maxStars = int.MaxValue;

    [ObservableProperty] private string _starsHint = string.Empty;

    public ObservableCollection<string> SubjectNames { get; } = new();

    public bool ShowSubject => StarsRequired > 0;
    public bool ShowSubjectName => StarsRequired > 0 && Subject != SubjectType.None;

    partial void OnUnlockPriceChanged(int value)
    {
        if (value < 0)
        {
            UnlockPrice = 0;
            return;
        }
        if (_loading) return;
        _data.UnlockPrice = value;
        _changed();
    }

    partial void OnStarsRequiredChanged(int value)
    {
        if (_loading) return;
        int stars = Math.Clamp(value, 0, MaxStars);
        _data.StarsRequired = stars;
        if (stars != value)
        {
            _loading = true;
            StarsRequired = stars;
            _loading = false;
        }
        _changed();
    }

    partial void OnSubjectChanged(SubjectType value)
    {
        RefreshSubjectNames();
        if (_loading) return;
        _data.Subject = value;
        SubjectName = SubjectNames.Contains(_data.SubjectName) ? _data.SubjectName : SubjectNames.FirstOrDefault();
        UpdateLimit();
        _changed();
    }

    partial void OnSubjectNameChanged(string? value)
    {
        if (_loading || value == null) return;
        _data.SubjectName = value;
        UpdateLimit();
        _changed();
    }

    /// <summary>Recomputes the star limit and clamps the current value to it.</summary>
    private void UpdateLimit()
    {
        if (_maxStarsFunc == null)
        {
            MaxStars = Subject == SubjectType.Track ? 3 : int.MaxValue;
        }
        else
        {
            MaxStars = _maxStarsFunc(Subject, Subject == SubjectType.None ? null : SubjectName);
        }
        StarsHint = MaxStars == int.MaxValue ? string.Empty : Subject switch
        {
            SubjectType.Track => "0 to 3 stars for a track.",
            SubjectType.Location => $"At most {MaxStars} (3 × the location's Story and Bonus levels).",
            _ => $"At most {MaxStars} (3 × all Story and Bonus levels).",
        };
        if (StarsRequired > MaxStars)
            StarsRequired = MaxStars;
    }

    private void RefreshSubjectNames()
    {
        SubjectNames.Clear();
        var source = Subject switch
        {
            SubjectType.Track => _tracks(),
            SubjectType.Location => _locations(),
            _ => Enumerable.Empty<string>(),
        };
        foreach (var name in source)
            SubjectNames.Add(name);
    }
}

/// <summary>A trick of Shop_payed.xml with its display name and track image (shared by levels and the picker).</summary>
public sealed partial class TrickInfo : ObservableObject
{
    public TrickInfo(string name, string displayName, string trackImage)
    {
        Name = name;
        DisplayName = string.IsNullOrEmpty(displayName) ? name : displayName;
        TrackImageName = trackImage;
    }

    public string Name { get; }
    public string DisplayName { get; }
    public string TrackImageName { get; }

    [ObservableProperty] private Bitmap? _image;

    /// <summary>Tricks of a project, with their images loading in the background (icons/tricks/TRACK_*.png).</summary>
    public static List<TrickInfo> Load(ProjectInfo project, ShopService shop, LocalizationService localization)
    {
        var list = shop.GetItems(ShopService.TrickGroup)
            .Select(i => new TrickInfo(i.Name, localization.Get(i.Name) ?? string.Empty, i.TrackImage))
            .ToList();
        _ = LoadImagesAsync(project, list);
        return list;
    }

    private static async Task LoadImagesAsync(ProjectInfo project, List<TrickInfo> list)
    {
        string folder = VPath.Combine(project.Path, "icons", "tricks");
        foreach (var info in list)
        {
            if (string.IsNullOrEmpty(info.TrackImageName)) continue;
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
            {
                string path = VPath.Combine(folder, info.TrackImageName + ext);
                if (!await AppServices.FileSystem.FileExistsAsync(path)) continue;
                try { info.Image = await ImageLoader.LoadAsync(AppServices.FileSystem, path, 117); }
                catch { /* shown as "No Image" */ }
                break;
            }
        }
    }
}

/// <summary>Edits a level's track data for one mode (common or hunter).</summary>
public sealed partial class TrackEditorViewModel : ObservableObject
{
    private readonly TrackData _data;
    private readonly Action _changed;
    private bool _loading;

    public TrackEditorViewModel(TrackData data, Action changed, Func<IEnumerable<string>> tracks, Func<IEnumerable<string>> locations,
        IReadOnlyList<TrickInfo> availableTricks, IReadOnlyList<string> starsTemplates, IReadOnlyList<string> rewardTemplates, IReadOnlyList<string> videos,
        Func<SubjectType, string?, int>? maxStars = null)
    {
        _data = data;
        _changed = changed;
        AvailableTricks = availableTricks;
        StarsTemplates = starsTemplates;
        RewardTemplates = rewardTemplates;
        Videos = videos;
        Unlock = new UnlockEditorViewModel(data.Unlock, changed, tracks, locations, maxStars);

        _loading = true;
        foreach (var trick in data.Tricks)
            Tricks.Add(new TrickSlotViewModel(trick, Find(trick)));
        StarsTemplate = data.StarsTemplate;
        RewardTemplate = data.RewardTemplate;
        VideoStart = videos.Contains(data.VideoStart) ? data.VideoStart : "None";
        VideoEnd = videos.Contains(data.VideoEnd) ? data.VideoEnd : "None";
        _loading = false;
    }

    public UnlockEditorViewModel Unlock { get; }
    public IReadOnlyList<TrickInfo> AvailableTricks { get; }
    public IReadOnlyList<string> StarsTemplates { get; }
    public IReadOnlyList<string> RewardTemplates { get; }
    public IReadOnlyList<string> Videos { get; }

    public ObservableCollection<TrickSlotViewModel> Tricks { get; } = new();

    [ObservableProperty] private string? _starsTemplate;
    [ObservableProperty] private string? _rewardTemplate;
    [ObservableProperty] private string? _videoStart;
    [ObservableProperty] private string? _videoEnd;

    partial void OnStarsTemplateChanged(string? value) { if (!_loading && value != null) { _data.StarsTemplate = value; _changed(); } }
    partial void OnRewardTemplateChanged(string? value) { if (!_loading && value != null) { _data.RewardTemplate = value; _changed(); } }
    partial void OnVideoStartChanged(string? value) { if (!_loading && value != null) { _data.VideoStart = value; _changed(); } }
    partial void OnVideoEndChanged(string? value) { if (!_loading && value != null) { _data.VideoEnd = value; _changed(); } }

    private TrickInfo? Find(string name) => AvailableTricks.FirstOrDefault(t => t.Name == name);

    public bool HasTrick(string name) => Tricks.Any(t => t.Name == name);

    /// <summary>Picker toggle: adds the trick at the end, or removes it.</summary>
    public void SetTrick(string name, bool include)
    {
        var existing = Tricks.FirstOrDefault(t => t.Name == name);
        if (include && existing == null)
            Tricks.Add(new TrickSlotViewModel(name, Find(name)));
        else if (!include && existing != null)
            Tricks.Remove(existing);
        else
            return;
        SyncTricks();
    }

    /// <summary>"Add Trick": a picker window with every trick of the project; changes apply right away.</summary>
    [RelayCommand]
    private async Task AddTrickAsync()
    {
        await AppServices.Dialogs.ShowAsync(new TrickPickerDialogViewModel(this));
    }

    [RelayCommand]
    private void RemoveTrick(TrickSlotViewModel? slot)
    {
        if (slot == null) return;
        Tricks.Remove(slot);
        SyncTricks();
    }

    internal void SyncTricks()
    {
        if (_loading) return;
        _data.Tricks = Tricks.Select(t => t.Name).Where(n => n.Length > 0).ToList();
        _changed();
    }
}

/// <summary>One trick of a level: its track image and display name (the internal name when it isn't in the shop).</summary>
public sealed class TrickSlotViewModel
{
    public TrickSlotViewModel(string name, TrickInfo? info)
    {
        Name = name;
        Info = info;
    }

    public string Name { get; }
    public TrickInfo? Info { get; }
    public string DisplayName => Info?.DisplayName ?? Name;
    public bool IsMissing => Info == null;
}

/// <summary>A trick tile of the picker.</summary>
public sealed partial class TrickChoiceViewModel : ObservableObject
{
    private readonly Action<TrickChoiceViewModel, bool> _changed;
    private bool _syncing;

    public TrickChoiceViewModel(TrackEditorViewModel track, TrickInfo info)
        : this(info, track.HasTrick(info.Name), (c, on) => track.SetTrick(c.Info.Name, on)) { }

    public TrickChoiceViewModel(TrickInfo info, bool selected, Action<TrickChoiceViewModel, bool> changed)
    {
        Info = info;
        _isSelected = selected;
        _changed = changed;
    }

    public TrickInfo Info { get; }

    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (!_syncing)
            _changed(this, value);
    }

    /// <summary>Changes the check without notifying (single-choice pickers).</summary>
    public void Sync(bool value)
    {
        _syncing = true;
        IsSelected = value;
        _syncing = false;
    }

    [RelayCommand]
    private void Toggle() => IsSelected = !IsSelected;
}

/// <summary>Grid of every trick; clicking one adds it to (or removes it from) the level immediately.</summary>
public sealed partial class TrickPickerDialogViewModel : DialogViewModel
{
    public TrickPickerDialogViewModel(TrackEditorViewModel track)
    {
        Title = "Select Tricks";
        Choices = track.AvailableTricks.Select(t => new TrickChoiceViewModel(track, t)).ToList();
    }

    /// <summary>Single choice (an Area's trick): picking one replaces the previous one right away.</summary>
    public TrickPickerDialogViewModel(IEnumerable<TrickInfo> tricks, string? current, Action<string?> set)
    {
        Title = "Select Trick";
        Hint = "Click a trick to use it for this Area (only one). Changes apply right away.";
        var list = new List<TrickChoiceViewModel>();
        foreach (var info in tricks)
        {
            list.Add(new TrickChoiceViewModel(info, info.Name == current, (choice, on) =>
            {
                foreach (var other in list.Where(c => !ReferenceEquals(c, choice)))
                    other.Sync(false);
                set(on ? choice.Info.Name : null);
            }));
        }
        Choices = list;
    }

    public string Hint { get; } = "Click a trick to add it to the level or remove it. Changes apply right away.";

    public IReadOnlyList<TrickChoiceViewModel> Choices { get; }
    public bool IsEmpty => Choices.Count == 0;

    public override bool Resizable => true;
    public override double PreferredWidth => 640;
    public override double PreferredHeight => 520;

    [RelayCommand]
    private void Done() => Close(true);
}
