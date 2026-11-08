using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Services;
using Vectorier.Services.IO;
using Vectorier.Services.Localization;
using Vectorier.Services.Projects;

namespace Vectorier.ViewModels.ProjectManager;

// ===================================================================== GENERIC FILE LISTS

public sealed partial class FileItemViewModel : ObservableObject
{
    public FileItemViewModel(string path, bool isProtected, string? displayName = null)
    {
        Path = path;
        IsProtected = isProtected;
        DisplayName = displayName ?? VPath.GetFileName(path);
    }

    public string Path { get; }
    public string DisplayName { get; }
    public string FileName => VPath.GetFileName(Path);
    public string Name => VPath.GetFileNameWithoutExtension(Path);
    public bool IsProtected { get; }

    [ObservableProperty] private bool _isPlaying;
}

/// <summary>Models, Musics, Sounds, Videos and XML pages: a single list of files with import and delete.</summary>
public sealed partial class FileListPageViewModel : PmPageViewModel
{
    public override System.Windows.Input.ICommand? DeleteShortcut => DeleteSelectedCommand;

    private readonly string _folder;
    private readonly string[] _extensions;

    /// <summary>Audio the game cannot play (Musics and Sounds take WAV only): a warning names them.</summary>
    private static readonly string[] OtherAudio = { ".mp3", ".ogg", ".flac", ".m4a", ".aac", ".wma", ".opus", ".aif", ".aiff", ".mid", ".midi" };
    private readonly HashSet<string> _protected;
    private readonly bool _includeLevels;
    private readonly List<FileItemViewModel> _all = new();

    private FileListPageViewModel(ProjectManagerViewModel owner, string title, string icon, string folder, string[] extensions,
        IEnumerable<string>? protectedNames = null, bool canPlay = false, bool includeLevels = false, string? protectedReason = null)
        : base(owner, title, icon)
    {
        _folder = folder;
        _extensions = extensions;
        _protected = new HashSet<string>(protectedNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        CanPlay = canPlay;
        _includeLevels = includeLevels;
        ProtectedReason = protectedReason ?? Loc.T("This file is required by the game and cannot be deleted.");
    }

    public static FileListPageViewModel Models(ProjectManagerViewModel o) =>
        new(o, Loc.T("Models"), "Models", "models", new[] { ".xml" }, new[] { "0", "1" });

    public static FileListPageViewModel Musics(ProjectManagerViewModel o) =>
        new(o, Loc.T("Musics"), "Music", "music", new[] { ".wav" }, new[] { "menu" }, canPlay: true);

    public static FileListPageViewModel Sounds(ProjectManagerViewModel o) =>
        new(o, Loc.T("Sounds"), "Sounds", "sounds", new[] { ".wav" }, new[]
        {
            "bonus_pickup", "cash_register", "enemy_charge", "enemy_discharge",
            "glass_break", "glass_item_drop1", "glass_item_drop2", "glass_item_drop3",
            "papers1", "papers2", "trick_activate", "ui_button_big_release",
            "ui_button_big_toggle", "ui_button_round_toggle", "ui_button_square_toggle",
            "ui_click", "ui_window_options", "ui_window_profile", "ui_window_shop"
        }, canPlay: true);

    public static FileListPageViewModel Videos(ProjectManagerViewModel o) =>
        new(o, Loc.T("Videos"), "Videos", "videos", new[] { ".mp4" }, new[] { "intro" });

    public static FileListPageViewModel XmlFiles(ProjectManagerViewModel o) =>
        new(o, Loc.T("XML Files"), "Xml", "xmlroot", new[] { ".xml" }, new[] { "TriggerTemplates" }, includeLevels: true,
            protectedReason: Loc.T("TriggerTemplates.xml cannot be deleted. Importing a file with the same name replaces it."));

    public override bool HasSearch => true;
    public bool CanPlay { get; }
    public string ProtectedReason { get; }
    private string FolderPath => VPath.Combine(Project.Path, _folder);

    public ObservableCollection<FileItemViewModel> Items { get; } = new();
    public ObservableCollection<FileItemViewModel> SelectedItems { get; } = new();

    [ObservableProperty] private string _countText = string.Empty;

    protected override async Task LoadAsync()
    {
        _all.Clear();
        var files = await AppServices.FileSystem.GetFilesAsync(FolderPath, recursive: _includeLevels);
        foreach (var file in files)
        {
            if (!VPath.HasExtension(file, _extensions))
                continue;
            string relative = file.Substring(FolderPath.Length).TrimStart('/');
            // XML page: xmlroot files plus the levels folder; skip editor caches and other sub folders.
            if (_includeLevels && relative.Contains('/') && !relative.StartsWith("levels/", StringComparison.OrdinalIgnoreCase))
                continue;
            _all.Add(new FileItemViewModel(file, _protected.Contains(VPath.GetFileNameWithoutExtension(file)) && !relative.Contains('/'), relative));
        }
        ApplyFilter();
    }

    public string DisplayPath(FileItemViewModel item) => item.Path.Substring(FolderPath.Length).TrimStart('/');

    protected override void ApplyFilter()
    {
        Items.Clear();
        foreach (var item in _all.Where(i => Matches(i.DisplayName)))
            Items.Add(item);
        CountText = Loc.F("Files: {0}", Items.Count);
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var topLevel = AppServices.Shell.CurrentTopLevel;
        if (topLevel == null)
            return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.F("Import {0}", Title),
            AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType(Title) { Patterns = _extensions.Select(e => "*" + e).ToArray() } },
        });
        await ImportItemsAsync(files);
    }

    public async Task ImportItemsAsync(IEnumerable<IStorageItem> items)
    {
        try
        {
            var replace = new ReplaceAsker(Loc.T("Import"));
            var refused = new List<string>();
            foreach (var (file, _) in await TextureImport.CollectAsync(items))
            {
                if (!VPath.HasExtension(file.Name, _extensions))
                {
                    if (CanPlay && VPath.HasExtension(file.Name, OtherAudio))
                        refused.Add(file.Name);
                    continue;
                }
                string target = VPath.Combine(FolderPath, file.Name);
                bool exists = await AppServices.FileSystem.FileExistsAsync(target);
                if (exists && !await replace.AskAsync(Loc.F("'{0}' already exists. Replace it?", file.Name)))
                    continue;
                await StorageImport.CopyFileAsync(AppServices.FileSystem, file, target);
            }
            // Musics and Sounds: the game plays WAV files only.
            if (refused.Count > 0)
                await AppServices.Dialogs.ShowMessageAsync(Loc.T("Import"),
                    Loc.F("Only WAV files are supported. Not imported:\n{0}", string.Join("\n", refused.Take(10)) + (refused.Count > 10 ? "\n…" : "")),
                    MessageKind.Warning);
            if (_folder == "models")
                ProjectContext.For(Project).Models.Invalidate();
            if (_folder == "xmlroot")
                ProjectContext.For(Project).Objects.Invalidate();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(Loc.T("Import"), ex);
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        var selected = SelectedItems.ToList();
        if (selected.Count == 0)
            return;
        var deletable = selected.Where(s => !s.IsProtected).ToList();
        if (deletable.Count == 0)
        {
            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Delete"), ProtectedReason, MessageKind.Info);
            return;
        }
        string skipped = deletable.Count < selected.Count ? $"\n({selected.Count - deletable.Count} protected file(s) will be kept.)" : "";
        if (!await AppServices.Dialogs.ConfirmAsync(Loc.T("Delete"), Loc.F("Delete {0} file(s)?{1}", deletable.Count, skipped), Loc.T("Delete"), Loc.T("Cancel"), MessageKind.Warning))
            return;
        foreach (var item in deletable)
            await AppServices.FileSystem.DeleteFileAsync(item.Path);
        await LoadAsync();
    }

    private const string VolumeSetting = "SoundPreviewVolume";

    /// <summary>Preview volume, 0–100 (shared by Sounds and Musics, remembered between sessions).</summary>
    [ObservableProperty] private double _volume = ReadVolume();

    private static double ReadVolume() =>
        double.TryParse(AppServices.Platform.Settings.Get(VolumeSetting), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double v) ? Math.Clamp(v, 0, 100) : 80;

    partial void OnVolumeChanged(double value)
    {
        AppServices.Platform.Settings.Set(VolumeSetting, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AppServices.Platform.SetSoundVolume(value / 100.0);
    }

    private FileItemViewModel? _playing;

    // Concurrent: the buttons stay enabled while a sound plays (another one replaces it, the same one stops it).
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task PlayAsync(FileItemViewModel? item)
    {
        if (item == null)
            return;
        bool wasPlaying = item.IsPlaying;
        StopPlayback();
        if (wasPlaying)
            return;
        item.IsPlaying = true;
        _playing = item;
        await AppServices.Platform.PlaySoundAsync(AppServices.FileSystem, item.Path, Volume / 100.0);
        // Ended (or stopped): back to the play icon, unless another sound has started since.
        if (ReferenceEquals(_playing, item))
        {
            item.IsPlaying = false;
            _playing = null;
        }
    }

    private void StopPlayback()
    {
        _playing = null;
        foreach (var other in _all)
            other.IsPlaying = false;
        AppServices.Platform.StopSound();
    }

    public override void OnDeactivated()
    {
        if (CanPlay)
            StopPlayback();
    }
}
