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

namespace Vectorier.ViewModels.ProjectManager;

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
}
