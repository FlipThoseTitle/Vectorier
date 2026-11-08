using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Models.Scene;
using Vectorier.Services;
using Vectorier.Services.IO;
using Vectorier.Services.Localization;
using Vectorier.Services.Projects;
using Vectorier.ViewModels.Dialogs;

namespace Vectorier.ViewModels.Editor;

/// <summary>An editable file name in a Sets list.</summary>
public sealed partial class SetEntry : ObservableObject
{
    public SetEntry(string file) => _file = file;

    [ObservableProperty] private string _file;

    /// <summary>Re-reads the value once the dropdown has its choices.</summary>
    public void Refresh() => OnPropertyChanged(nameof(File));
}

/// <summary>Level Settings window (Unity's ExportConfig without file/export options).</summary>
public sealed partial class LevelSettingsViewModel : DialogViewModel
{
    private readonly EditorViewModel _editor;
    private readonly LevelSettings _settings;

    public LevelSettingsViewModel(EditorViewModel editor, LevelSettings settings)
    {
        _editor = editor;
        _settings = settings;
        IsObjectMode = editor.IsEditingObject;
        Title = IsObjectMode ? Loc.F("Object Settings - {0}", editor.ObjectEdit!.Name) : Loc.F("Level Settings - {0}", editor.Document.LevelName);

        SpawnNames = editor.Document.AllElements().OfType<SpawnElement>().Select(sp => sp.Name)
            .Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();

        // City / Ground are automatic: the files of the objects used in the level.
        var (autoCity, autoGround) = editor.ComputeAutoSets();
        foreach (var f in autoCity) CitySets.Add(new SetEntry(f));
        foreach (var f in autoGround) GroundSets.Add(new SetEntry(f));
        foreach (var f in settings.LibrarySets) LibrarySets.Add(new SetEntry(f));
        _musicName = settings.MusicName;
        _coinValue = settings.CoinValue;
        foreach (var m in settings.CommonModels) CommonModels.Add(new ModelDefinitionViewModel(m.Clone(), this, false));
        foreach (var m in settings.HunterModels) HunterModels.Add(new ModelDefinitionViewModel(m.Clone(), this, true));
        _selectedModel = CommonModels.FirstOrDefault();
        CommonModels.CollectionChanged += (_, _) => OnModelRenamed();
        HunterModels.CollectionChanged += (_, _) => OnModelRenamed();
        _ = LoadChoicesAsync();
        if (!IsObjectMode)
            _ = LoadThumbnailAsync();
        CloseRequested += StopMusicPreview;
    }

    // ================= MUSIC PREVIEW ================= //

    /// <summary>The music is playing in the preview.</summary>
    [ObservableProperty] private bool _isMusicPlaying;

    /// <summary>Fixed preview volume, independent of the level's music volume.</summary>
    private const double MusicPreviewVolume = 0.8;

    private int _playVersion;

    /// <summary>Plays the selected music (or stops it when it is playing).</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ToggleMusicPreview()
    {
        if (IsMusicPlaying)
        {
            StopMusicPreview();
            return;
        }
        string? name = MusicName?.Trim();
        if (string.IsNullOrEmpty(name))
            return;
        string folder = VPath.Combine(_editor.Project.Path, "music");
        string? path = (await AppServices.FileSystem.GetFilesAsync(folder))
            .FirstOrDefault(f => string.Equals(VPath.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase));
        if (path == null)
        {
            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Music Preview"), Loc.F("'{0}' was not found in the project's music folder.", name), MessageKind.Warning);
            return;
        }
        int version = ++_playVersion;
        IsMusicPlaying = true;
        try
        {
            await AppServices.Platform.PlaySoundAsync(AppServices.FileSystem, path, MusicPreviewVolume);
        }
        finally
        {
            if (version == _playVersion)
                IsMusicPlaying = false;
        }
    }

    private void StopMusicPreview()
    {
        _playVersion++;
        if (IsMusicPlaying)
            AppServices.Platform.StopSound();
        IsMusicPlaying = false;
    }

    public override bool Resizable => true;
    public override double PreferredWidth => 820;
    public override double PreferredHeight => 700;

    public ProjectInfo Project => _editor.Project;

    /// <summary>Opened from Edit Object mode: only what applies to an object is shown (no thumbnail, music, coins, models).</summary>
    public bool IsObjectMode { get; }
    public bool IsLevelMode => !IsObjectMode;
    public string LevelName => _editor.Document.LevelName;

    public ObservableCollection<SetEntry> CitySets { get; } = new();
    public ObservableCollection<SetEntry> GroundSets { get; } = new();
    public ObservableCollection<SetEntry> LibrarySets { get; } = new();
    public ObservableCollection<string> XmlFiles { get; } = new();
    public ObservableCollection<string> MusicChoices { get; } = new();

    public ObservableCollection<ModelDefinitionViewModel> CommonModels { get; } = new();
    public ObservableCollection<ModelDefinitionViewModel> HunterModels { get; } = new();

    /// <summary>Spawn names of the level, each once.</summary>
    public IReadOnlyList<string> SpawnNames { get; }

    [ObservableProperty] private string _musicName;

    partial void OnMusicNameChanged(string value) => StopMusicPreview();
    [ObservableProperty] private int _coinValue;
    [ObservableProperty] private ModelDefinitionViewModel? _selectedModel;
    [ObservableProperty] private string? _fileToAdd;
    [ObservableProperty] private Avalonia.Media.Imaging.Bitmap? _thumbnail;
    [ObservableProperty] private string _thumbnailStatus = string.Empty;

    /// <summary>The Models tab edits Hunter mode's models instead of the common ones.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentModels), nameof(ModeTitle))]
    private bool _isHunterMode;

    public ObservableCollection<ModelDefinitionViewModel> CurrentModels => IsHunterMode ? HunterModels : CommonModels;
    public string ModeTitle => IsHunterMode ? Loc.T("Hunter Mode Models") : Loc.T("Common Mode Models");

    partial void OnIsHunterModeChanged(bool value)
    {
        // The list must switch before the selection, or it would clear the new selection.
        OnPropertyChanged(nameof(CurrentModels));
        SelectedModel = CurrentModels.FirstOrDefault();
    }

    partial void OnCoinValueChanged(int value)
    {
        if (value < 0) CoinValue = 0;
    }

    public List<string> ModelNames(bool hunter) =>
        (hunter ? HunterModels : CommonModels).Select(m => m.Name).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();

    public void OnModelRenamed()
    {
        foreach (var m in CommonModels.Concat(HunterModels))
            m.RefreshChoices();
    }

    private async Task LoadChoicesAsync()
    {
        try
        {
            var project = _editor.Project;
            foreach (var file in (await AppServices.FileSystem.GetFilesAsync(project.XmlRoot))
                         .Where(f => VPath.HasExtension(f, ".xml"))
                         .Select(VPath.GetFileName)
                         .Where(f => !string.Equals(f, "TriggerTemplates.xml", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                XmlFiles.Add(file);
            // Files already used stay selectable even when they are missing.
            foreach (var used in CitySets.Concat(GroundSets).Concat(LibrarySets).Select(e => e.File))
                if (!string.IsNullOrEmpty(used) && !XmlFiles.Contains(used))
                    XmlFiles.Add(used);
            foreach (var entry in CitySets.Concat(GroundSets).Concat(LibrarySets))
                entry.Refresh();

            string musicFolder = VPath.Combine(project.Path, "music");
            foreach (var file in (await AppServices.FileSystem.GetFilesAsync(musicFolder))
                         .Select(VPath.GetFileNameWithoutExtension).Distinct().OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                MusicChoices.Add(file);
            if (!string.IsNullOrEmpty(MusicName) && !MusicChoices.Contains(MusicName))
                MusicChoices.Add(MusicName);
            OnPropertyChanged(nameof(MusicName));
        }
        catch
        {
            // Choices are a convenience.
        }
    }

    // ================= THUMBNAIL ================= //

    private LevelThumbnail Thumbnails => new(_editor.Project, LevelName);

    private async Task LoadThumbnailAsync()
    {
        try
        {
            Thumbnail = await Thumbnails.LoadAsync(512);
            ThumbnailStatus = Thumbnail == null ? Loc.T("No thumbnail yet") : string.Empty;
        }
        catch
        {
            Thumbnail = null;
        }
    }

    [RelayCommand]
    private async Task PickThumbnailAsync()
    {
        var topLevel = AppServices.Shell.CurrentTopLevel;
        if (topLevel == null)
            return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = Loc.T("Select Level Thumbnail (512 × 340 recommended)"),
            AllowMultiple = false,
            FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType(Loc.T("Images")) { Patterns = new[] { "*.png", "*.jpg", "*.jpeg" } } },
        });
        if (files.Count == 0)
            return;
        // The crop window always opens: the thumbnail is saved at exactly 512 × 340.
        byte[]? png = await Services.Assets.ImageCrop.CropAsync(files[0], Services.Assets.ImageTarget.LevelThumbnail);
        if (png == null)
            return;
        try
        {
            await Thumbnails.SavePngAsync(png);
            await LoadThumbnailAsync();
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Thumbnail"), ex.Message, MessageKind.Error);
        }
    }

    [RelayCommand]
    private async Task CaptureThumbnailAsync()
    {
        var capture = new ThumbnailCaptureViewModel(_editor);
        if (await AppServices.Dialogs.ShowAsync(capture) is true)
            await LoadThumbnailAsync();
    }

    // ================= SETS ================= //

    [RelayCommand]
    private void AddSet(string? list)
    {
        var target = list switch { "City" => CitySets, "Ground" => GroundSets, _ => LibrarySets };
        target.Add(new SetEntry(FileToAdd ?? (list == "City" ? "buildings.xml" : "objects.xml")));
    }

    [RelayCommand]
    private void RemoveSet(SetEntry? entry)
    {
        if (entry == null) return;
        CitySets.Remove(entry);
        GroundSets.Remove(entry);
        LibrarySets.Remove(entry);
    }

    [RelayCommand]
    private void MoveSetUp(SetEntry? entry) => MoveSet(entry, -1);

    [RelayCommand]
    private void MoveSetDown(SetEntry? entry) => MoveSet(entry, 1);

    private void MoveSet(SetEntry? entry, int delta)
    {
        if (entry == null) return;
        foreach (var list in new[] { CitySets, GroundSets, LibrarySets })
        {
            int i = list.IndexOf(entry);
            if (i < 0) continue;
            int j = Math.Clamp(i + delta, 0, list.Count - 1);
            if (i != j) list.Move(i, j);
            return;
        }
    }

    // ================= MODELS ================= //

    [RelayCommand]
    private void AddModel()
    {
        var model = new ModelDefinitionViewModel(new LevelModelDefinition
        {
            Attributes = new List<KeyValuePair<string, string>>
            {
                new("Name", "NewModel"), new("Type", "0"), new("Color", "0"),
                new("BirthSpawn", SpawnNames.FirstOrDefault() ?? "DefaultSpawn"), new("AI", "0"), new("Time", "0"),
            },
        }, this, IsHunterMode);
        CurrentModels.Add(model);
        SelectedModel = model;
    }

    [RelayCommand]
    private void RemoveModel()
    {
        if (SelectedModel != null)
            CurrentModels.Remove(SelectedModel);
        SelectedModel = CurrentModels.FirstOrDefault();
    }

    [RelayCommand]
    private void MoveModel(string? direction)
    {
        var list = CurrentModels;
        if (SelectedModel == null) return;
        int i = list.IndexOf(SelectedModel);
        int j = Math.Clamp(i + (direction == "Up" ? -1 : 1), 0, list.Count - 1);
        if (i >= 0 && i != j)
        {
            var keep = SelectedModel;
            list.Move(i, j);
            SelectedModel = keep;
        }
    }

    /// <summary>Restores the default Player and Hunter of both modes, after a confirmation.</summary>
    [RelayCommand]
    private async Task ResetModelsAsync()
    {
        bool ok = await AppServices.Dialogs.ConfirmAsync(Loc.T("Default Models"),
            Loc.T("Replace the models of both Common Mode and Hunter Mode with the default Player and Hunter?\nEvery model you configured in this level is removed."),
            Loc.T("Use Defaults"), Loc.T("Cancel"), MessageKind.Warning);
        if (!ok)
            return;
        CommonModels.Clear();
        foreach (var m in LevelSettings.ParseModels(LevelSettings.DefaultCommonModels)) CommonModels.Add(new ModelDefinitionViewModel(m, this, false));
        HunterModels.Clear();
        foreach (var m in LevelSettings.ParseModels(LevelSettings.DefaultHunterModels)) HunterModels.Add(new ModelDefinitionViewModel(m, this, true));
        SelectedModel = CurrentModels.FirstOrDefault();
    }

    [RelayCommand]
    private void Apply()
    {
        _settings.CitySets = CitySets.Select(s => s.File.Trim()).Where(s => s.Length > 0).ToList();
        _settings.GroundSets = GroundSets.Select(s => s.File.Trim()).Where(s => s.Length > 0).ToList();
        _settings.LibrarySets = LibrarySets.Select(s => s.File.Trim()).Where(s => s.Length > 0).ToList();
        _settings.MusicName = MusicName?.Trim() ?? string.Empty;
        _settings.CoinValue = Math.Max(0, CoinValue);
        _settings.CommonModels = CommonModels.Select(m => m.Model).ToList();
        _settings.HunterModels = HunterModels.Select(m => m.Model).ToList();
        Close(_settings);
    }

    [RelayCommand]
    private void CancelDialog() => Close(null);
}
