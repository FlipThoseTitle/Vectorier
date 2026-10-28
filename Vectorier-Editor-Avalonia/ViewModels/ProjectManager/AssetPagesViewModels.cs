using System;
using System.Collections.Generic;
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

// ===================================================================== TEXTURES

public sealed partial class TextureItemViewModel : ObservableObject
{
    public TextureItemViewModel(TextureEntry entry, TextureLibrary library)
    {
        Entry = entry;
        Library = library;
    }

    public TextureEntry Entry { get; }
    public TextureLibrary Library { get; }
    public string Name => Entry.Name;
    public string FileName => Entry.FileName;
    public bool IsAnimated => Entry.IsAnimated;
    public bool IsProtected => TextureLibrary.IsProtected(Entry);

    [ObservableProperty] private bool _isSelected;
}

/// <summary>Textures page: one grid with every static and animated texture of the project.</summary>
public sealed partial class TexturesPageViewModel : PmPageViewModel
{
    private readonly List<TextureItemViewModel> _all = new();

    public TexturesPageViewModel(ProjectManagerViewModel owner) : base(owner, "Textures", "Textures") { }

    public override bool HasSearch => true;

    public TextureLibrary Library => ProjectContext.For(Project).Textures;

    public ObservableCollection<TextureItemViewModel> Items { get; } = new();
    public ObservableCollection<TextureItemViewModel> SelectedItems { get; } = new();

    [ObservableProperty] private string _countText = string.Empty;

    protected override async Task LoadAsync()
    {
        await Library.RefreshIndexAsync();
        _all.Clear();
        SelectedItems.Clear();
        _anchor = null;
        // Every file is listed here, including the game's own sprite sheets (they are protected, not hidden).
        foreach (var entry in Library.Entries)
            _all.Add(new TextureItemViewModel(entry, Library));
        ApplyFilter();
    }

    protected override void ApplyFilter()
    {
        var list = _all.Where(i => Matches(i.FileName)).ToList();
        Items.Clear();
        foreach (var item in list)
            Items.Add(item);
        foreach (var gone in SelectedItems.Where(s => !list.Contains(s)).ToList())
        {
            gone.IsSelected = false;
            SelectedItems.Remove(gone);
        }
        CountText = $"{Items.Count} texture{(Items.Count == 1 ? "" : "s")}";
        ItemsRebuilt?.Invoke();
    }

    /// <summary>The grid rebuilds its rows (only visible rows are created).</summary>
    public event Action? ItemsRebuilt;

    private TextureItemViewModel? _anchor;

    /// <summary>Click: select only this one. Ctrl: add or remove it. Shift: the range from the last clicked tile.</summary>
    public void Click(TextureItemViewModel item, bool ctrl, bool shift)
    {
        if (shift && _anchor != null && Items.Contains(_anchor))
        {
            int a = Items.IndexOf(_anchor), b = Items.IndexOf(item);
            if (a > b) (a, b) = (b, a);
            if (!ctrl) ClearSelection();
            for (int i = a; i <= b; i++)
                Select(Items[i], true);
            return;
        }
        if (ctrl)
        {
            Select(item, !item.IsSelected);
        }
        else
        {
            ClearSelection();
            Select(item, true);
        }
        _anchor = item;
    }

    private void Select(TextureItemViewModel item, bool on)
    {
        item.IsSelected = on;
        if (on && !SelectedItems.Contains(item)) SelectedItems.Add(item);
        else if (!on) SelectedItems.Remove(item);
    }

    public void ClearSelection()
    {
        foreach (var s in SelectedItems) s.IsSelected = false;
        SelectedItems.Clear();
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in Items) Select(item, true);
    }

    [RelayCommand]
    private async Task ImportFilesAsync()
    {
        var topLevel = AppServices.Shell.CurrentTopLevel;
        if (topLevel == null)
            return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Textures (select an image and its .plist together for animated textures)",
            AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType("Textures") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.plist", "*.json" } } },
        });
        if (files.Count > 0)
            await ImportItemsAsync(files);
    }

    [RelayCommand]
    private async Task ImportFolderAsync()
    {
        var topLevel = AppServices.Shell.CurrentTopLevel;
        if (topLevel == null || !topLevel.StorageProvider.CanPickFolder)
            return;
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Import a folder of textures", AllowMultiple = false });
        if (folders.Count > 0)
            await ImportItemsAsync(folders);
    }

    /// <summary>Imports dropped or picked files and folders (folders are processed recursively).</summary>
    public async Task ImportItemsAsync(IEnumerable<IStorageItem> items)
    {
        IsLoading = true;
        try
        {
            var files = await TextureImport.CollectAsync(items);
            await Library.ImportAsync(files.Select(f => (f.File.Name, (Func<Task<byte[]>>)(() => StorageImport.ReadAllBytesAsync(f.File)))).ToList(),
                name => AppServices.Dialogs.ConfirmAsync("Import Textures", $"'{name}' already exists. Replace it?", "Replace", "Skip", MessageKind.Warning));
            await LoadAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Import Textures", ex);
        }
        finally
        {
            IsLoading = false;
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
            await AppServices.Dialogs.ShowMessageAsync("Delete Textures", "The selected textures are used by the game itself and cannot be deleted.", MessageKind.Info);
            return;
        }
        string list = string.Join(", ", deletable.Take(5).Select(d => d.FileName)) + (deletable.Count > 5 ? $" and {deletable.Count - 5} more" : "");
        if (!await AppServices.Dialogs.ConfirmAsync("Delete Textures", $"Delete {list}?\nLevels that use them will show them as missing.", "Delete", "Cancel", MessageKind.Warning))
            return;

        foreach (var item in deletable)
            await Library.DeleteAsync(item.Entry);
        await LoadAsync();
    }
}

public static class TextureImport
{
    /// <summary>Flattens picked/dropped items into files (recursing into folders).</summary>
    public static async Task<List<(IStorageFile File, string Folder)>> CollectAsync(IEnumerable<IStorageItem> items)
    {
        var result = new List<(IStorageFile, string)>();
        foreach (var item in items)
        {
            if (item is IStorageFile file)
                result.Add((file, string.Empty));
            else if (item is IStorageFolder folder)
            {
                foreach (var (f, relative) in await StorageImport.ListFilesAsync(folder))
                    result.Add((f, VPath.Combine(folder.Name, relative)));
            }
        }
        return result;
    }
}

// ===================================================================== ANIMATIONS

/// <summary>Animations page: the moves XML and the list of animation .bin files.</summary>
public sealed partial class AnimationsPageViewModel : PmPageViewModel
{
    public const string MovesFileName = "moves.xml";
    private readonly List<string> _all = new();

    public AnimationsPageViewModel(ProjectManagerViewModel owner) : base(owner, "Animations", "Animations")
    {
        SelectedItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CanPreview));
    }

    /// <summary>One .bin selected: it can be previewed on the character.</summary>
    public bool CanPreview => SelectedItems.Count == 1 && VPath.HasExtension(SelectedItems[0], ".bin", ".bytes");

    /// <summary>Plays the selected animation on models/0.xml + 1.xml, the camera following the Camera node.</summary>
    [RelayCommand]
    private async Task PreviewAsync()
    {
        if (!CanPreview)
            return;
        string name = SelectedItems[0];
        await ViewModels.Preview.CharacterPreviewViewModel.ShowAsync(Project, $"Preview - {name}",
            new[] { Services.Assets.CharacterModel.DefaultSkin }, VPath.Combine(DataFolder, name), ViewModels.Preview.PreviewCamera.FollowCameraNode);
    }

    public override bool HasSearch => true;

    private string DataFolder => VPath.Combine(Project.AnimationsFolder, "data");
    private string MovesPath => VPath.Combine(Project.AnimationsFolder, MovesFileName);

    public ObservableCollection<string> Items { get; } = new();
    public ObservableCollection<string> SelectedItems { get; } = new();

    [ObservableProperty] private string _movesFile = "None";
    [ObservableProperty] private string _countText = string.Empty;

    protected override async Task LoadAsync()
    {
        MovesFile = await AppServices.FileSystem.FileExistsAsync(MovesPath) ? MovesFileName : "None";
        _all.Clear();
        foreach (var file in await AppServices.FileSystem.GetFilesAsync(DataFolder))
        {
            if (VPath.HasExtension(file, ".bin", ".bytes"))
                _all.Add(VPath.GetFileName(file));
        }
        ApplyFilter();
    }

    protected override void ApplyFilter()
    {
        Items.Clear();
        foreach (var name in _all.Where(Matches))
            Items.Add(name);
        CountText = $"{Items.Count} animation{(Items.Count == 1 ? "" : "s")}";
    }

    /// <summary>Replaces animations/moves.xml (always saved under that name, whatever the source is called).</summary>
    [RelayCommand]
    private async Task ChooseMovesAsync()
    {
        var topLevel = AppServices.Shell.CurrentTopLevel;
        if (topLevel == null)
            return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select the Moves XML",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Moves XML") { Patterns = new[] { "*.xml" } } },
        });
        if (files.Count == 0)
            return;
        try
        {
            await StorageImport.CopyFileAsync(AppServices.FileSystem, files[0], MovesPath);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Moves XML", ex);
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var topLevel = AppServices.Shell.CurrentTopLevel;
        if (topLevel == null)
            return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Animations",
            AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType("Animation") { Patterns = new[] { "*.bin", "*.bytes" } } },
        });
        await ImportItemsAsync(files);
    }

    public async Task ImportItemsAsync(IEnumerable<IStorageItem> items)
    {
        try
        {
            foreach (var (file, _) in await TextureImport.CollectAsync(items))
            {
                if (VPath.HasExtension(file.Name, ".bin", ".bytes"))
                    await StorageImport.CopyFileAsync(AppServices.FileSystem, file, VPath.Combine(DataFolder, file.Name));
                else if (string.Equals(file.Name, MovesFileName, StringComparison.OrdinalIgnoreCase))
                    await StorageImport.CopyFileAsync(AppServices.FileSystem, file, MovesPath);
            }
            await LoadAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Import Animations", ex);
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        var selected = SelectedItems.ToList();
        if (selected.Count == 0)
            return;
        if (!await AppServices.Dialogs.ConfirmAsync("Delete Animations", $"Delete {selected.Count} animation file(s)?", "Delete", "Cancel", MessageKind.Warning))
            return;
        foreach (var name in selected)
            await AppServices.FileSystem.DeleteFileAsync(VPath.Combine(DataFolder, name));
        await LoadAsync();
    }
}

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
    private readonly string _folder;
    private readonly string[] _extensions;
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
        ProtectedReason = protectedReason ?? "This file is required by the game and cannot be deleted.";
    }

    public static FileListPageViewModel Models(ProjectManagerViewModel o) =>
        new(o, "Models", "Models", "models", new[] { ".xml" }, new[] { "0", "1" });

    public static FileListPageViewModel Musics(ProjectManagerViewModel o) =>
        new(o, "Musics", "Music", "music", new[] { ".wav", ".mp3", ".ogg" }, new[] { "menu" }, canPlay: true);

    public static FileListPageViewModel Sounds(ProjectManagerViewModel o) =>
        new(o, "Sounds", "Sounds", "sounds", new[] { ".wav" }, new[]
        {
            "bonus_pickup", "cash_register", "enemy_charge", "enemy_discharge",
            "glass_break", "glass_item_drop1", "glass_item_drop2", "glass_item_drop3",
            "papers1", "papers2", "trick_activate", "ui_button_big_release",
            "ui_button_big_toggle", "ui_button_round_toggle", "ui_button_square_toggle",
            "ui_click", "ui_window_options", "ui_window_profile", "ui_window_shop"
        }, canPlay: true);

    public static FileListPageViewModel Videos(ProjectManagerViewModel o) =>
        new(o, "Videos", "Videos", "videos", new[] { ".mp4" }, new[] { "intro" });

    public static FileListPageViewModel XmlFiles(ProjectManagerViewModel o) =>
        new(o, "XML Files", "Xml", "xmlroot", new[] { ".xml" }, new[] { "TriggerTemplates" }, includeLevels: true,
            protectedReason: "TriggerTemplates.xml cannot be deleted. Importing a file with the same name replaces it.");

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
        CountText = $"{Items.Count} file{(Items.Count == 1 ? "" : "s")}";
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var topLevel = AppServices.Shell.CurrentTopLevel;
        if (topLevel == null)
            return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import " + Title,
            AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType(Title) { Patterns = _extensions.Select(e => "*" + e).ToArray() } },
        });
        await ImportItemsAsync(files);
    }

    public async Task ImportItemsAsync(IEnumerable<IStorageItem> items)
    {
        try
        {
            foreach (var (file, _) in await TextureImport.CollectAsync(items))
            {
                if (!VPath.HasExtension(file.Name, _extensions))
                    continue;
                string target = VPath.Combine(FolderPath, file.Name);
                bool exists = await AppServices.FileSystem.FileExistsAsync(target);
                if (exists && !await AppServices.Dialogs.ConfirmAsync("Import", $"'{file.Name}' already exists. Replace it?", "Replace", "Skip", MessageKind.Warning))
                    continue;
                await StorageImport.CopyFileAsync(AppServices.FileSystem, file, target);
            }
            if (_folder == "models")
                ProjectContext.For(Project).Models.Invalidate();
            if (_folder == "xmlroot")
                ProjectContext.For(Project).Objects.Invalidate();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Import", ex);
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
            await AppServices.Dialogs.ShowMessageAsync("Delete", ProtectedReason, MessageKind.Info);
            return;
        }
        string skipped = deletable.Count < selected.Count ? $"\n({selected.Count - deletable.Count} protected file(s) will be kept.)" : "";
        if (!await AppServices.Dialogs.ConfirmAsync("Delete", $"Delete {deletable.Count} file(s)?{skipped}", "Delete", "Cancel", MessageKind.Warning))
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

// ===================================================================== TRICKS / GEARS

public sealed partial class ShopItemViewModel : ObservableObject
{
    public ShopItemViewModel(ShopItem item, string displayName)
    {
        Item = item;
        _displayName = displayName;
    }

    public ShopItem Item { get; }
    public string Name => Item.Name;

    [ObservableProperty] private string _displayName;
    [ObservableProperty] private Bitmap? _shopImage;
    [ObservableProperty] private Bitmap? _trackImage;
    [ObservableProperty] private bool _hasModel;

    public void RaiseName() => OnPropertyChanged(nameof(Name));
}

/// <summary>Shared logic of the Tricks and Gears pages (Shop_payed.xml groups + localization + icons).</summary>
public abstract partial class ShopPageViewModel : PmPageViewModel
{
    protected ShopPageViewModel(ProjectManagerViewModel owner, string title, string icon) : base(owner, title, icon) { }

    public override bool HasSearch => true;

    protected abstract string Group { get; }
    protected abstract string Prefix { get; }
    protected abstract bool HasTrackImage { get; }

    protected string ShopIcons => VPath.Combine(Project.Path, "icons", "shop");
    protected string TrickIcons => VPath.Combine(Project.Path, "icons", "tricks");

    protected readonly List<ShopItemViewModel> All = new();

    public ObservableCollection<ShopItemViewModel> Items { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand), nameof(MoveUpCommand), nameof(MoveDownCommand))]
    private ShopItemViewModel? _selectedItem;

    [ObservableProperty] private string _editName = string.Empty;
    [ObservableProperty] private string _editDisplayName = string.Empty;
    [ObservableProperty] private int _editPrice;

    private bool _suppress;
    public bool HasSelection => SelectedItem != null;

    partial void OnSelectedItemChanged(ShopItemViewModel? value)
    {
        _suppress = true;
        EditName = value?.Name ?? string.Empty;
        EditDisplayName = value?.DisplayName ?? string.Empty;
        EditPrice = value?.Item.Price ?? 0;
        _suppress = false;
    }

    protected override async Task LoadAsync()
    {
        await Owner.EnsureSharedDataAsync();
        string? keep = SelectedItem?.Name;
        All.Clear();
        foreach (var item in Owner.Shop.GetItems(Group))
        {
            var vm = new ShopItemViewModel(item, Owner.Localization.Get(item.Name) ?? string.Empty);
            All.Add(vm);
            _ = LoadImagesAsync(vm);
        }
        ApplyFilter();
        SelectedItem = Items.FirstOrDefault(i => i.Name == keep);
    }

    protected override void ApplyFilter()
    {
        Items.Clear();
        foreach (var item in All.Where(i => Matches(i.Name) || Matches(i.DisplayName)))
            Items.Add(item);
    }

    protected async Task<string?> FindImageAsync(string folder, string name)
    {
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
        {
            string path = VPath.Combine(folder, name + ext);
            if (await AppServices.FileSystem.FileExistsAsync(path))
                return path;
        }
        return null;
    }

    protected virtual async Task LoadImagesAsync(ShopItemViewModel vm)
    {
        string? shop = await FindImageAsync(ShopIcons, vm.Item.ShopImage);
        vm.ShopImage = shop == null ? null : await ImageLoader.LoadAsync(AppServices.FileSystem, shop, 256);
        if (HasTrackImage)
        {
            string? track = await FindImageAsync(TrickIcons, vm.Item.TrackImage);
            vm.TrackImage = track == null ? null : await ImageLoader.LoadAsync(AppServices.FileSystem, track, 117);
        }
    }

    protected async Task SaveAsync()
    {
        Owner.Shop.SetItems(Group, All.Select(a => a.Item), HasTrackImage);
        await Owner.Shop.SaveAsync();
        await Owner.Localization.SaveAsync();
    }

    public string FormatName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;
        string name = input.Trim().ToUpperInvariant().Replace(' ', '_');
        return name.StartsWith(Prefix, StringComparison.Ordinal) ? name : Prefix + name;
    }

    protected abstract Task RenameAssetsAsync(ShopItemViewModel item, string oldName, string newName);

    [RelayCommand]
    public async Task CommitNameAsync()
    {
        var item = SelectedItem;
        if (item == null || _suppress)
            return;
        string formatted = FormatName(EditName);
        if (formatted.Length == 0 || formatted == item.Name)
        {
            EditName = item.Name;
            return;
        }
        if (All.Any(a => a != item && a.Name == formatted))
        {
            await AppServices.Dialogs.ShowMessageAsync(Title, $"'{formatted}' already exists.", MessageKind.Warning);
            EditName = item.Name;
            return;
        }
        try
        {
            string old = item.Name;
            await RenameAssetsAsync(item, old, formatted);
            item.Item.Name = formatted;
            Owner.Localization.Rename(old, formatted);
            item.RaiseName();
            EditName = formatted;
            await SaveAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(Title, ex);
        }
    }

    [RelayCommand]
    public async Task CommitDetailsAsync()
    {
        var item = SelectedItem;
        if (item == null || _suppress)
            return;
        bool changed = false;
        if (EditDisplayName != item.DisplayName)
        {
            item.DisplayName = EditDisplayName;
            Owner.Localization.Set(item.Name, EditDisplayName);
            changed = true;
        }
        if (EditPrice != item.Item.Price)
        {
            item.Item.Price = Math.Max(0, EditPrice);
            changed = true;
        }
        if (changed)
            await SaveAsync();
    }

    partial void OnEditPriceChanged(int value)
    {
        if (!_suppress)
            _ = CommitDetailsAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RemoveAsync()
    {
        var item = SelectedItem;
        if (item == null)
            return;
        if (!await AppServices.Dialogs.ConfirmAsync(Title, $"Remove '{item.Name}' and its images?", "Remove", "Cancel", MessageKind.Warning))
            return;
        await DeleteAssetsAsync(item);
        Owner.Localization.Remove(item.Name);
        All.Remove(item);
        SelectedItem = null;
        ApplyFilter();
        await SaveAsync();
    }

    protected abstract Task DeleteAssetsAsync(ShopItemViewModel item);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task MoveUpAsync() => MoveAsync(-1);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task MoveDownAsync() => MoveAsync(1);

    private async Task MoveAsync(int direction)
    {
        var item = SelectedItem;
        if (item == null)
            return;
        int index = All.IndexOf(item);
        int target = index + direction;
        if (target < 0 || target >= All.Count)
            return;
        All.RemoveAt(index);
        All.Insert(target, item);
        ApplyFilter();
        SelectedItem = item;
        await SaveAsync();
    }

    [RelayCommand]
    private async Task ChangeShopImageAsync()
    {
        var item = SelectedItem;
        if (item == null) return;
        var file = await PickImageAsync("Select Shop Image (256 × 226 recommended)");
        if (file == null) return;
        if (string.IsNullOrEmpty(item.Item.ShopImage)) item.Item.ShopImage = "SHOP_" + item.Name;
        await ReplaceImageAsync(ShopIcons, item.Item.ShopImage, file);
        await LoadImagesAsync(item);
        await SaveAsync();
    }

    protected async Task<IStorageFile?> PickImageAsync(string title)
    {
        var topLevel = AppServices.Shell.CurrentTopLevel;
        if (topLevel == null)
            return null;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg" } } },
        });
        return files.Count > 0 ? files[0] : null;
    }

    protected async Task ReplaceImageAsync(string folder, string name, IStorageFile file)
    {
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
            await AppServices.FileSystem.DeleteFileAsync(VPath.Combine(folder, name + ext));
        string extension = VPath.GetExtension(file.Name).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg")) extension = ".png";
        await StorageImport.CopyFileAsync(AppServices.FileSystem, file, VPath.Combine(folder, name + extension));
    }

    protected async Task RenameImageAsync(string folder, string oldName, string newName)
    {
        string? path = await FindImageAsync(folder, oldName);
        if (path != null)
            await AppServices.FileSystem.MoveFileAsync(path, VPath.Combine(folder, newName + VPath.GetExtension(path)), true);
    }
}

public sealed partial class TricksPageViewModel : ShopPageViewModel
{
    public TricksPageViewModel(ProjectManagerViewModel owner) : base(owner, "Tricks", "Tricks") { }

    protected override string Group => ShopService.TrickGroup;
    protected override string Prefix => "TRICK_";
    protected override bool HasTrackImage => true;

    [RelayCommand]
    private async Task AddAsync()
    {
        string name = "TRICK_NEW";
        for (int i = 1; All.Any(a => a.Name == name); i++)
            name = $"TRICK_NEW_{i}";
        var item = new ShopItem { Name = name, ShopImage = "SHOP_" + name, TrackImage = "TRACK_" + name };
        var vm = new ShopItemViewModel(item, string.Empty);
        All.Add(vm);
        ApplyFilter();
        SelectedItem = vm;
        await SaveAsync();
    }

    protected override async Task RenameAssetsAsync(ShopItemViewModel item, string oldName, string newName)
    {
        await RenameImageAsync(ShopIcons, item.Item.ShopImage, "SHOP_" + newName);
        await RenameImageAsync(TrickIcons, item.Item.TrackImage, "TRACK_" + newName);
        item.Item.ShopImage = "SHOP_" + newName;
        item.Item.TrackImage = "TRACK_" + newName;
    }

    protected override async Task DeleteAssetsAsync(ShopItemViewModel item)
    {
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
        {
            await AppServices.FileSystem.DeleteFileAsync(VPath.Combine(ShopIcons, item.Item.ShopImage + ext));
            await AppServices.FileSystem.DeleteFileAsync(VPath.Combine(TrickIcons, item.Item.TrackImage + ext));
        }
    }


    [RelayCommand]
    private async Task ChangeTrackImageAsync()
    {
        var item = SelectedItem;
        if (item == null) return;
        var file = await PickImageAsync("Select Track Image (117 × 117)");
        if (file == null) return;
        if (string.IsNullOrEmpty(item.Item.TrackImage)) item.Item.TrackImage = "TRACK_" + item.Name;
        await ReplaceImageAsync(TrickIcons, item.Item.TrackImage, file);
        await LoadImagesAsync(item);
        await SaveAsync();
    }
}

public sealed partial class GearsPageViewModel : ShopPageViewModel
{
    public GearsPageViewModel(ProjectManagerViewModel owner) : base(owner, "Gears", "Gears") { }

    protected override string Group => ShopService.ClothingGroup;
    protected override string Prefix => "GEAR_";
    protected override bool HasTrackImage => false;

    private string ModelPath(string name) => VPath.Combine(Project.ModelsFolder, name + ".xml");

    protected override async Task LoadImagesAsync(ShopItemViewModel vm)
    {
        await base.LoadImagesAsync(vm);
        vm.HasModel = await AppServices.FileSystem.FileExistsAsync(ModelPath(vm.Name));
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var topLevel = AppServices.Shell.CurrentTopLevel;
        if (topLevel == null)
            return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select the gear's model XML",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Model XML") { Patterns = new[] { "*.xml" } } },
        });

        string name = files.Count > 0 ? FormatName(VPath.GetFileNameWithoutExtension(files[0].Name)) : "GEAR_NEW";
        string unique = name;
        for (int i = 1; All.Any(a => a.Name == unique); i++)
            unique = $"{name}_{i}";

        try
        {
            if (files.Count > 0)
            {
                await StorageImport.CopyFileAsync(AppServices.FileSystem, files[0], ModelPath(unique));
                ProjectContext.For(Project).Models.Invalidate(unique);
            }
            var item = new ShopItem { Name = unique, ShopImage = "SHOP_" + unique };
            var vm = new ShopItemViewModel(item, files.Count > 0 ? VPath.GetFileNameWithoutExtension(files[0].Name) : string.Empty);
            if (vm.DisplayName.Length > 0)
                Owner.Localization.Set(unique, vm.DisplayName);
            All.Add(vm);
            ApplyFilter();
            SelectedItem = vm;
            _ = LoadImagesAsync(vm);
            await SaveAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(Title, ex);
        }
    }

    [RelayCommand]
    private async Task ChangeModelAsync()
    {
        var item = SelectedItem;
        var topLevel = AppServices.Shell.CurrentTopLevel;
        if (item == null || topLevel == null)
            return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select the gear's model XML",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Model XML") { Patterns = new[] { "*.xml" } } },
        });
        if (files.Count == 0)
            return;
        await StorageImport.CopyFileAsync(AppServices.FileSystem, files[0], ModelPath(item.Name));
        ProjectContext.For(Project).Models.Invalidate(item.Name);
        await LoadImagesAsync(item);
    }

    protected override async Task RenameAssetsAsync(ShopItemViewModel item, string oldName, string newName)
    {
        if (await AppServices.FileSystem.FileExistsAsync(ModelPath(oldName)))
            await AppServices.FileSystem.MoveFileAsync(ModelPath(oldName), ModelPath(newName), true);
        await RenameImageAsync(ShopIcons, item.Item.ShopImage, "SHOP_" + newName);
        item.Item.ShopImage = "SHOP_" + newName;
        ProjectContext.For(Project).Models.Invalidate();
    }

    protected override async Task DeleteAssetsAsync(ShopItemViewModel item)
    {
        await AppServices.FileSystem.DeleteFileAsync(ModelPath(item.Name));
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
            await AppServices.FileSystem.DeleteFileAsync(VPath.Combine(ShopIcons, item.Item.ShopImage + ext));
        ProjectContext.For(Project).Models.Invalidate(item.Name);
    }

}
