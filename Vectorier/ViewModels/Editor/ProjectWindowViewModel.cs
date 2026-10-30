using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Models.Scene;
using Vectorier.Services;
using Vectorier.Services.Assets;
using Vectorier.Services.IO;
using Vectorier.Services.Projects;
using Vectorier.Services.Xml;
using Vectorier.ViewModels.ProjectManager;

namespace Vectorier.ViewModels.Editor;

public enum PwContext
{
    /// <summary>Premade objects from xmlroot files.</summary>
    Objects,
    /// <summary>Textures with cached virtual folders (Image, Animation, Particle).</summary>
    Textures,
    Triggers,
    Areas,
    Models,
    Items,
    /// <summary>Platform surfaces: Rough, Smooth and Block (dressed with block textures).</summary>
    Platforms,
    /// <summary>Trapezoid, Camera, Spawn, Comment: nothing to browse.</summary>
    None
}

/// <summary>How a click changes the Project Window selection (Ctrl toggles, Shift selects a range).</summary>
public enum PwSelect
{
    Replace,
    Toggle,
    Range,
}

/// <summary>Several selected tiles dragged together (to a folder; never placed in the Scene).</summary>
public sealed record PwItemGroup(IReadOnlyList<PwItem> Items);

/// <summary>Left panel entry: an objects XML file, a cached folder, or the Unorganized bucket.</summary>
public sealed partial class PwFolder : ObservableObject
{
    public PwFolder(string name, string? fileName = null, bool isUnorganized = false)
    {
        Name = name;
        FileName = fileName;
        IsUnorganized = isUnorganized;
    }

    public string Name { get; }

    /// <summary>For the Objects context: the XML file name inside xmlroot.</summary>
    public string? FileName { get; }

    public bool IsUnorganized { get; }

    public bool IsFile => FileName != null;

    public bool IsUserFolder => !IsUnorganized && FileName == null;

    [ObservableProperty] private bool _isSelected;

    /// <summary>Object XML type (null until checked): Building files use In / Out / Box attributes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TypeBadge), nameof(HasTypeBadge))]
    private bool? _isBuilding;

    public string TypeBadge => IsBuilding == true ? "Building" : "Object";
    public bool HasTypeBadge => IsFile && IsBuilding != null;
}

/// <summary>Right panel entry.</summary>
public sealed partial class PwItem : ObservableObject
{
    public PwItem(string key, string label)
    {
        Key = key;
        Label = label;
    }

    /// <summary>Identity used by cached folders.</summary>
    public string Key { get; }

    public string Label { get; }

    public ObjectDefinition? Definition { get; init; }
    public TextureEntry? Texture { get; init; }
    public StoredTrigger? Trigger { get; init; }
    public StoredArea? Area { get; init; }
    public string? ModelName { get; init; }
    public ItemType? ItemType { get; init; }
    public PlatformSurfaceType? PlatformSurface { get; init; }

    /// <summary>Texture shown as preview (textures and items).</summary>
    public string? PreviewTexture { get; init; }

    public bool IsComment => Trigger == null && Area == null;

    [ObservableProperty] private bool _isSelected;

    /// <summary>False for models that cannot be rendered (they cannot be placed either).</summary>
    [ObservableProperty] private bool _canPlace = true;

    [ObservableProperty] private string? _status;

    /// <summary>Model preview (filled lazily when the tile becomes visible).</summary>
    [ObservableProperty] private Geometry? _silhouette;

    public bool PreviewRequested { get; set; }

    public bool ShowModelPlaceholder => ModelName != null && Silhouette == null;

    partial void OnSilhouetteChanged(Geometry? value) => OnPropertyChanged(nameof(ShowModelPlaceholder));

    public string ToolTip => Status is { Length: > 0 } s ? $"{Label}\n{s}" : Label;

    partial void OnStatusChanged(string? value) => OnPropertyChanged(nameof(ToolTip));
}

/// <summary>
/// The Project Window: a folder list on the left and the selected folder's content on the right. What it shows
/// depends on the element type chosen in the Element Bar.
/// </summary>
public sealed partial class ProjectWindowViewModel : ObservableObject
{
    public const string TexturesKey = "Textures";
    public const string TriggersKey = "Triggers";
    public const string AreasKey = "Areas";
    public const string ModelsKey = "Models";

    private readonly EditorViewModel _editor;
    private ElementType _elementType = ElementType.Object;
    private int _loadVersion;
    private int _reloadVersion;
    private readonly List<PwItem> _allItems = new();

    public ProjectWindowViewModel(EditorViewModel editor)
    {
        _editor = editor;
    }

    private ProjectContext Assets => _editor.Context;
    private EditorCache Cache => Assets.EditorCache;

    public TextureLibrary Textures => Assets.Textures;

    /// <summary>Folders shown on the left (filtered by <see cref="FolderSearchText"/>).</summary>
    public ObservableCollection<PwFolder> Folders { get; } = new();
    public ObservableCollection<PwItem> Items { get; } = new();

    /// <summary>Every folder of the context, also the ones hidden by the left search.</summary>
    private readonly List<PwFolder> _allFolders = new();
    public IReadOnlyList<PwFolder> AllFolders => _allFolders;

    private readonly List<PwItem> _selectedItems = new();
    private readonly List<PwFolder> _selectedFolders = new();
    private PwItem? _itemAnchor;
    private PwFolder? _folderAnchor;
    private bool _selectingItems;
    private bool _selectingFolders;

    /// <summary>Selected tiles (several with Ctrl / Shift: for organizing only, never placed).</summary>
    public IReadOnlyList<PwItem> SelectedItems => _selectedItems;

    /// <summary>Selected folders / Object XML files (several with Ctrl / Shift).</summary>
    public IReadOnlyList<PwFolder> SelectedFolders => _selectedFolders;

    public bool HasMultipleItems => _selectedItems.Count > 1;
    public bool HasMultipleFolders => _selectedFolders.Count > 1;

    /// <summary>Where the last click in the Project Window was (Delete acts there).</summary>
    public bool LastClickInFolders { get; set; }

    [ObservableProperty] private string _folderSearchText = string.Empty;

    partial void OnFolderSearchTextChanged(string value) => ApplyFolderFilter();

    private void ApplyFolderFilter()
    {
        string search = FolderSearchText.Trim();
        var shown = search.Length == 0 ? _allFolders : _allFolders.Where(f => f.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || (f.FileName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        if (shown.Count == Folders.Count && shown.SequenceEqual(Folders))
            return;
        Folders.Clear();
        foreach (var folder in shown)
            Folders.Add(folder);
    }

    /// <summary>Title of the right panel: the folder, or how many are selected.</summary>
    public string ContentTitle => _selectedFolders.Count > 1
        ? $"{_selectedFolders.Count} {(Context == PwContext.Objects ? "files" : "folders")} selected"
        : SelectedFolder?.Name ?? string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsFolders), nameof(ShowsItems), nameof(CanCreateFolder), nameof(IsTextureContext),
        nameof(IsObjectContext), nameof(IsModelContext), nameof(IsItemContext), nameof(IsStoredContext), nameof(EmptyText), nameof(AcceptsFileDrop),
        nameof(CanImport), nameof(ImportLabel), nameof(SupportsMultipleItems))]
    private PwContext _context = PwContext.Objects;

    [ObservableProperty] private PwFolder? _selectedFolder;
    [ObservableProperty] private PwItem? _selectedItem;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _header = "Project";
    [ObservableProperty] private double _tileSize = 96;

    public bool ShowsFolders => Context is PwContext.Objects or PwContext.Textures or PwContext.Triggers or PwContext.Areas or PwContext.Models;
    public bool ShowsItems => Context != PwContext.None;
    public bool CanCreateFolder => ShowsFolders;
    public bool IsTextureContext => Context == PwContext.Textures;
    public bool IsObjectContext => Context == PwContext.Objects;
    public bool IsModelContext => Context == PwContext.Models;
    public bool IsItemContext => Context == PwContext.Items;
    public bool IsStoredContext => Context is PwContext.Triggers or PwContext.Areas;
    public bool AcceptsFileDrop => Context is PwContext.Textures or PwContext.Models;

    /// <summary>Image / Animation / Particle and Model assets can be imported into the project (not Objects).</summary>
    public bool CanImport => Context is PwContext.Textures or PwContext.Models;

    public string ImportLabel => Context == PwContext.Models ? "Import Models..." : "Import Textures...";

    public string EmptyText => Context switch
    {
        PwContext.None => _elementType switch
        {
            ElementType.Platform => "Click in the Scene to add a 100 × 100 Platform, or drag to size it.",
            ElementType.Trapezoid => "Click in the Scene to add a Trapezoid. Resizing keeps the 26.565° slope; hold Ctrl to size freely.",
            ElementType.Camera => "Click in the Scene to add a Camera (one per game mode).",
            ElementType.Spawn => "Click in the Scene to add a Spawn.",
            ElementType.Comment => "Click in the Scene to add a Comment, or drag to size it. Its text is its name.",
            _ => string.Empty,
        },
        PwContext.Objects => HasMultipleFolders ? "Several files are selected: drag them to reorder, or press Delete to remove them."
            : SelectedFolder == null ? "Select an XML file on the left." : "This file has no objects. Drag elements from the Hierarchy here to save them.",
        PwContext.Textures => "No textures here. Drag images or folders from your file explorer to import them.",
        PwContext.Triggers => "Drag a Trigger from the Hierarchy here to keep it for later. Click in the Scene to add a generic Trigger.",
        PwContext.Areas => "Drag an Area from the Hierarchy here to keep it for later. Click in the Scene to add a generic Area.",
        PwContext.Models => "No model files in this folder.",
        _ => string.Empty,
    };

    /// <summary>Element type the Project Window currently lists assets for.</summary>
    public ElementType ElementType => _elementType;

    public bool HasPlaceableSelection => !HasMultipleItems && SelectedItem is { CanPlace: true };

    public string? SelectedTextureName => Context == PwContext.Textures ? SelectedItem?.Texture?.Name : null;

    public string? SelectedModelName => Context == PwContext.Models && SelectedItem is { CanPlace: true } item ? item.ModelName : null;

    /// <summary>What a click in the Scene creates from the Project Window selection.</summary>
    public ElementSource? CurrentSource => HasMultipleItems ? null : SourceFor(SelectedItem);

    /// <summary>What an item creates (used for the drag preview too).</summary>
    public static ElementSource? SourceFor(PwItem? item) => item switch
    {
        null => null,
        { PlatformSurface: { } surface } => new ElementSource(PlatformSurface: surface, Definition: item.Definition),
        { Definition: { } def } => new ElementSource(Definition: def),
        { Texture: { } tex } => new ElementSource(TextureName: tex.Name),
        { Trigger: { } t } => new ElementSource(Trigger: t),
        { Area: { } a } => new ElementSource(Area: a),
        { ModelName: { } m, CanPlace: true } => new ElementSource(ModelName: m),
        { ItemType: { } it } => new ElementSource(ItemType: it),
        _ => null,
    };

    private Avalonia.Threading.DispatcherTimer? _searchTimer;

    /// <summary>Typing filters after a short pause (every keystroke would rebuild the grid).</summary>
    partial void OnSearchTextChanged(string value)
    {
        _searchTimer?.Stop();
        if (string.IsNullOrWhiteSpace(value))
        {
            ApplyFilter();
            return;
        }
        _searchTimer ??= new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(150), Avalonia.Threading.DispatcherPriority.Background,
            (_, _) => { _searchTimer!.Stop(); ApplyFilter(); });
        _searchTimer.Start();
    }

    partial void OnSelectedFolderChanged(PwFolder? oldValue, PwFolder? newValue)
    {
        if (_selectingFolders)
            return;
        SetFolderSelection(newValue == null ? Array.Empty<PwFolder>() : new[] { newValue });
        _folderAnchor = newValue;
        _ = LoadItemsAsync();
    }

    partial void OnSelectedItemChanged(PwItem? oldValue, PwItem? newValue)
    {
        if (_selectingItems)
            return;
        SetItemSelection(newValue == null ? Array.Empty<PwItem>() : new[] { newValue });
        _itemAnchor = newValue;
        AfterItemSelection();
    }

    private void SetItemSelection(IEnumerable<PwItem> items)
    {
        foreach (var old in _selectedItems)
            old.IsSelected = false;
        _selectedItems.Clear();
        foreach (var item in items)
        {
            if (_selectedItems.Contains(item))
                continue;
            _selectedItems.Add(item);
            item.IsSelected = true;
        }
    }

    private void SetFolderSelection(IEnumerable<PwFolder> folders)
    {
        foreach (var old in _selectedFolders)
            old.IsSelected = false;
        _selectedFolders.Clear();
        foreach (var folder in folders)
        {
            if (_selectedFolders.Contains(folder))
                continue;
            _selectedFolders.Add(folder);
            folder.IsSelected = true;
        }
        OnPropertyChanged(nameof(HasMultipleFolders));
        OnPropertyChanged(nameof(ContentTitle));
        OnPropertyChanged(nameof(EmptyText));
    }

    /// <summary>Several tiles can be selected everywhere assets are listed except Platforms (nothing to organize).</summary>
    public bool SupportsMultipleItems => Context is not (PwContext.Platforms or PwContext.None);

    /// <summary>Click on a tile: Replace, Ctrl = Toggle, Shift = Range (from the last clicked tile).</summary>
    public void SelectItem(PwItem? item, PwSelect mode = PwSelect.Replace)
    {
        if (item == null)
        {
            SelectedItem = null;
            return;
        }
        if (!SupportsMultipleItems)
            mode = PwSelect.Replace;
        _selectingItems = true;
        try
        {
            switch (mode)
            {
                case PwSelect.Toggle:
                {
                    var list = _selectedItems.ToList();
                    if (!list.Remove(item))
                        list.Add(item);
                    SetItemSelection(list);
                    _itemAnchor = item;
                    break;
                }
                case PwSelect.Range:
                {
                    var anchor = _itemAnchor != null && Items.Contains(_itemAnchor) ? _itemAnchor : item;
                    int a = Items.IndexOf(anchor), b = Items.IndexOf(item);
                    if (a < 0 || b < 0)
                        goto default;
                    SetItemSelection(Items.Skip(Math.Min(a, b)).Take(Math.Abs(a - b) + 1));
                    break;
                }
                default:
                    SetItemSelection(new[] { item });
                    _itemAnchor = item;
                    break;
            }
            SelectedItem = _selectedItems.Contains(item) ? item : _selectedItems.LastOrDefault();
        }
        finally
        {
            _selectingItems = false;
        }
        AfterItemSelection();
    }

    /// <summary>Click on a folder: Replace, Ctrl = Toggle, Shift = Range.</summary>
    public void SelectFolder(PwFolder? folder, PwSelect mode = PwSelect.Replace)
    {
        if (folder == null)
            return;
        _selectingFolders = true;
        try
        {
            switch (mode)
            {
                case PwSelect.Toggle:
                {
                    var list = _selectedFolders.ToList();
                    if (!list.Remove(folder) || list.Count == 0)
                        list.Add(folder);
                    SetFolderSelection(list);
                    _folderAnchor = folder;
                    break;
                }
                case PwSelect.Range:
                {
                    var anchor = _folderAnchor != null && Folders.Contains(_folderAnchor) ? _folderAnchor : folder;
                    int a = Folders.IndexOf(anchor), b = Folders.IndexOf(folder);
                    if (a < 0 || b < 0)
                        goto default;
                    SetFolderSelection(Folders.Skip(Math.Min(a, b)).Take(Math.Abs(a - b) + 1));
                    break;
                }
                default:
                    SetFolderSelection(new[] { folder });
                    _folderAnchor = folder;
                    break;
            }
            SelectedFolder = _selectedFolders.Contains(folder) ? folder : _selectedFolders.LastOrDefault();
        }
        finally
        {
            _selectingFolders = false;
        }
        OnPropertyChanged(nameof(ContentTitle));
        _ = LoadItemsAsync();
    }

    private void AfterItemSelection()
    {
        OnPropertyChanged(nameof(HasPlaceableSelection));
        OnPropertyChanged(nameof(HasMultipleItems));
        OnPropertyChanged(nameof(CurrentSource));
        var newValue = SelectedItem;
        if (HasMultipleItems)
        {
            // Several assets are for organizing (folders, delete): nothing is placed, no preview follows the cursor.
            if (_editor.PlacementType == _elementType)
                _editor.PlacementType = null;
            _editor.StatusText = $"{_selectedItems.Count} items selected";
            return;
        }

        if (newValue == null)
            return;
        if (Context == PwContext.Models && !newValue.CanPlace)
        {
            _editor.PlacementType = null;
            _editor.StatusText = $"{newValue.Label} cannot be rendered, so it cannot be placed.";
            return;
        }
        // Picking an asset arms placement: the next click in the Scene creates it.
        _editor.PlacementType = _elementType;
    }

    // ================= CONTEXT ================= //

    public void SetContext(ElementType type)
    {
        _elementType = type;
        var context = type switch
        {
            ElementType.Object => PwContext.Objects,
            ElementType.Image or ElementType.Animation or ElementType.Particle => PwContext.Textures,
            ElementType.Trigger => PwContext.Triggers,
            ElementType.Area => PwContext.Areas,
            ElementType.Model => PwContext.Models,
            ElementType.Item => PwContext.Items,
            ElementType.Platform => PwContext.Platforms,
            _ => PwContext.None,
        };
        Header = type switch
        {
            ElementType.Object => "Objects",
            ElementType.Image => "Images",
            ElementType.Animation => "Animations",
            ElementType.Particle => "Particles",
            ElementType.Trigger => "Triggers",
            ElementType.Area => "Areas",
            ElementType.Model => "Models",
            ElementType.Item => "Items",
            ElementType.Platform => "Platforms",
            _ => type.ToString(),
        };

        bool changed = context != Context;
        Context = context;
        OnPropertyChanged(nameof(EmptyText));
        if (changed || Folders.Count == 0)
            _ = ReloadAsync();
        else if (SelectedItem != null && Context != PwContext.None)
            _editor.PlacementType = SelectedItem.CanPlace ? type : null;
    }

    [RelayCommand]
    public async Task ReloadAsync()
    {
        int version = ++_reloadVersion;
        ++_loadVersion;
        string? previousFolder = SelectedFolder?.Name;
        var previousFiles = _selectedFolders.Select(f => f.Name).ToList();
        SelectedItem = null;
        Items.Clear();
        _allItems.Clear();
        Folders.Clear();
        _allFolders.Clear();
        SelectedFolder = null;

        try
        {
            IsBusy = true;
            await Cache.EnsureLoadedAsync();
            var folders = await BuildFoldersAsync();
            if (version != _reloadVersion)
                return;
            _allFolders.AddRange(folders);
            ApplyFolderFilter();
            var restored = _allFolders.Where(f => previousFiles.Contains(f.Name)).ToList();
            if (restored.Count > 1)
            {
                _selectingFolders = true;
                SetFolderSelection(restored);
                SelectedFolder = restored.FirstOrDefault(f => f.Name == previousFolder) ?? restored[0];
                _selectingFolders = false;
                await LoadItemsAsync();
            }
            else
            {
                SelectedFolder = _allFolders.FirstOrDefault(f => f.Name == previousFolder) ?? Folders.FirstOrDefault() ?? _allFolders.FirstOrDefault();
                if (SelectedFolder == null)
                    await LoadItemsAsync();
            }
        }
        catch (Exception ex)
        {
            _editor.StatusText = "Project Window: " + ex.Message;
        }
        finally
        {
            if (version == _reloadVersion)
                IsBusy = false;
        }
    }

    private async Task<List<PwFolder>> BuildFoldersAsync()
    {
        var result = new List<PwFolder>();
        switch (Context)
        {
            case PwContext.Objects:
            {
                var files = await ObjectFilesAsync();
                foreach (var file in files)
                    result.Add(new PwFolder(ReadableFileName(file), file));
                _ = DetectFileTypesAsync(result);
                break;
            }
            case PwContext.Textures:
            case PwContext.Triggers:
            case PwContext.Areas:
            case PwContext.Models:
            {
                string key = CacheKey;
                var keys = await AllItemKeysAsync();
                foreach (var folder in Cache.GetFolders(key))
                    result.Add(new PwFolder(folder.Name));
                bool hasUnorganized = keys.Any(k => Cache.FolderOf(key, k) == null);
                if (hasUnorganized || result.Count == 0)
                    result.Insert(0, new PwFolder(result.Count == 0 && !hasUnorganized ? "All" : "Unorganized", isUnorganized: true));
                break;
            }
        }
        return result;
    }

    private string CacheKey => Context switch
    {
        PwContext.Textures => TexturesKey,
        PwContext.Triggers => TriggersKey,
        PwContext.Areas => AreasKey,
        PwContext.Models => ModelsKey,
        _ => string.Empty,
    };

    /// <summary>xmlroot XML files except TriggerTemplates.xml, in the user's order.</summary>
    private async Task<List<string>> ObjectFilesAsync()
    {
        var files = (await AppServices.FileSystem.GetFilesAsync(_editor.Project.XmlRoot))
            .Where(f => VPath.HasExtension(f, ".xml"))
            .Select(VPath.GetFileName)
            .Where(f => !string.Equals(f, "TriggerTemplates.xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var order = Cache.Data.ObjectFileOrder;
        return files.OrderBy(f => order.IndexOf(f) is int i && i >= 0 ? i : int.MaxValue).ToList();
    }

    /// <summary>"objects_downtown.xml" → "Objects Downtown".</summary>
    public static string ReadableFileName(string file)
    {
        string stem = VPath.GetFileNameWithoutExtension(file);
        var words = stem.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]);
        return string.Join(" ", words);
    }

    private async Task<List<string>> AllItemKeysAsync() => Context switch
    {
        PwContext.Textures => TextureItems().Select(i => i.Key).ToList(),
        PwContext.Triggers => Cache.Data.Triggers.Select(t => t.Id).ToList(),
        PwContext.Areas => Cache.Data.Areas.Select(a => a.Id).ToList(),
        PwContext.Models => (await Assets.Models.GetModelFilesAsync()).Select(VPath.GetFileNameWithoutExtension).ToList(),
        _ => new List<string>(),
    };

    private IEnumerable<PwItem> TextureItems()
    {
        var entries = Textures.BrowsableEntries.ToList();
        var duplicateNames = entries.GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            // Same name with different extensions is allowed: show the extension to tell them apart.
            string label = duplicateNames.Contains(entry.Name) ? entry.FileName : entry.Name;
            yield return new PwItem(entry.FileName, label) { Texture = entry, PreviewTexture = entry.Name };
        }
    }

    private async Task LoadItemsAsync()
    {
        int version = ++_loadVersion;
        SelectedItem = null;
        _allItems.Clear();
        var folder = SelectedFolder;
        var folders = _selectedFolders.Count > 0 ? _selectedFolders.ToList() : folder != null ? new List<PwFolder> { folder } : new List<PwFolder>();
        var items = new List<PwItem>();

        try
        {
            switch (Context)
            {
                case PwContext.Objects when folders.Count > 1:
                    // Several files: organizing only, nothing listed.
                    break;
                case PwContext.Objects when folder?.FileName != null:
                {
                    var table = await Assets.Objects.ResolveForFileAsync(folder.FileName);
                    var reader = new SceneXmlReader(table);
                    foreach (var def in await Assets.Objects.GetFileDefinitionsAsync(folder.FileName))
                    {
                        reader.BuildDefinition(def);
                        items.Add(new PwItem(def.Name, def.Name) { Definition = def });
                    }
                    // Objects that contain themselves are loaded without the self-reference (see SceneXmlReader).
                    var cyclic = items.Where(i => SceneXmlReader.CyclicReferences.ContainsKey(i.Label)).Select(i => i.Label).ToList();
                    if (cyclic.Count > 0)
                        _editor.StatusText = $"Skipped a reference to itself inside: {string.Join(", ", cyclic)} (it would nest forever).";
                    break;
                }
                case PwContext.Textures:
                    if (!Textures.IsIndexed)
                        await Textures.RefreshIndexAsync();
                    items.AddRange(InFolders(TextureItems(), folders));
                    break;
                case PwContext.Triggers:
                    items.AddRange(InFolders(Cache.Data.Triggers.Select(t => new PwItem(t.Id, t.Name) { Trigger = t }), folders));
                    break;
                case PwContext.Areas:
                    items.AddRange(InFolders(Cache.Data.Areas.Select(a => new PwItem(a.Id, a.Name) { Area = a }), folders));
                    break;
                case PwContext.Models:
                {
                    // Files that cannot be rendered cannot be placed either: they are not listed.
                    var files = await Assets.Models.GetModelFilesAsync();
                    var names = files.Select(f => VPath.GetFileNameWithoutExtension(f))
                        .Where(n => !ModelLibrary.IsSkipped(n))
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var checks = await Task.WhenAll(names.Select(async n =>
                    {
                        try { return (await Assets.Models.GetAsync(n)) is { IsSelfContained: true } ? n : null; }
                        catch { return null; }
                    }));
                    var all = checks.Where(n => n != null).Select(n => new PwItem(n!, n!) { ModelName = n, CanPlace = false, Status = "Loading..." });
                    items.AddRange(InFolders(all, folders));
                    break;
                }
                case PwContext.Platforms:
                    foreach (var surface in Enum.GetValues<PlatformSurfaceType>())
                    {
                        var preview = await PlatformPreviewAsync(surface);
                        items.Add(new PwItem("Platform" + surface, surface.ToString()) { PlatformSurface = surface, Definition = preview });
                    }
                    break;
                case PwContext.Items:
                    items.Add(new PwItem("Bonus", "Bonus") { ItemType = Models.Scene.ItemType.Bonus, PreviewTexture = "bonus_v4" });
                    items.Add(new PwItem("Coins", "Coins") { ItemType = Models.Scene.ItemType.Coin, PreviewTexture = "credits" });
                    break;
            }
        }
        catch (Exception ex)
        {
            _editor.StatusText = "Project Window: " + ex.Message;
        }

        if (version != _loadVersion)
            return;
        _allItems.AddRange(items);
        ApplyFilter();
        OnPropertyChanged(nameof(EmptyText));
        ApplyPendingReveal();
    }

    // ================= REVEAL NEW ITEMS ================= //

    private HashSet<string>? _pendingReveal;

    /// <summary>Asks the view to scroll the tile grid to an item.</summary>
    public event Action<PwItem>? RevealRequested;

    /// <summary>
    /// Newly added / imported items (by item key): opens the folder holding the first one, selects every new item
    /// shown there and scrolls to the first of them.
    /// </summary>
    public async Task RevealNewAsync(IReadOnlyCollection<string> keys, string? objectFile = null)
    {
        if (keys.Count == 0)
            return;
        _pendingReveal = keys.ToHashSet(StringComparer.Ordinal);
        if (SearchText.Length > 0 && !_allItems.Any(i => _pendingReveal.Contains(i.Key)))
            SearchText = string.Empty; // a search hiding them would make the reveal pointless

        // The folder they are in: the object file, or the cached folder of the first key (Unorganized when none).
        PwFolder? target;
        if (objectFile != null)
        {
            target = _allFolders.FirstOrDefault(f => string.Equals(f.FileName, objectFile, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            string? folderName = Cache.FolderOf(CacheKey, keys.First());
            target = folderName == null ? _allFolders.FirstOrDefault(f => f.IsUnorganized) : _allFolders.FirstOrDefault(f => f.Name == folderName);
        }
        if (target != null && FolderSearchText.Length > 0 && !Folders.Contains(target))
            FolderSearchText = string.Empty;

        if (target != null && (!ReferenceEquals(SelectedFolder, target) || _selectedFolders.Count > 1))
            SelectedFolder = target; // loads its items, then applies the reveal
        else
            await LoadItemsAsync();
    }

    private void ApplyPendingReveal()
    {
        if (_pendingReveal is not { } keys)
            return;
        var found = Items.Where(i => keys.Contains(i.Key)).ToList();
        if (found.Count == 0)
            return; // not loaded here yet (another folder is loading): try again after it
        _pendingReveal = null;
        SelectItem(found[0]);
        if (SupportsMultipleItems)
            foreach (var item in found.Skip(1))
                SelectItem(item, PwSelect.Toggle);
        RevealRequested?.Invoke(found[0]);
    }

    /// <summary>Items in any of the folders (several folders selected: their items together).</summary>
    private IEnumerable<PwItem> InFolders(IEnumerable<PwItem> items, IReadOnlyList<PwFolder> folders)
    {
        if (folders.Count <= 1)
            return InFolder(items, folders.FirstOrDefault());
        var list = items.ToList();
        var keep = new HashSet<PwItem>(folders.SelectMany(f => InFolder(list, f)));
        return list.Where(keep.Contains);
    }

    private IEnumerable<PwItem> InFolder(IEnumerable<PwItem> items, PwFolder? folder)
    {
        string key = CacheKey;
        if (folder == null)
            return items;
        if (folder.IsUnorganized)
            return items.Where(i => Cache.FolderOf(key, i.Key) == null);
        var members = Cache.GetFolders(key).FirstOrDefault(f => f.Name == folder.Name)?.Items ?? new List<string>();
        var set = members.ToHashSet(StringComparer.Ordinal);
        return items.Where(i => set.Contains(i.Key));
    }

    private void ApplyFilter()
    {
        _searchTimer?.Stop();
        string search = SearchText.Trim();
        var filtered = search.Length == 0 ? _allItems.ToList() : _allItems.Where(i => i.Label.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        // Unchanged result (e.g. a reload with the same items): keep the grid as it is.
        if (filtered.Count == Items.Count && filtered.SequenceEqual(Items))
            return;
        Items.Clear();
        foreach (var item in filtered)
            Items.Add(item);
    }

    // ================= MODELS ================= //

    /// <summary>Called when a model tile scrolls into view: validates and builds its silhouette once.</summary>
    public async void RequestModelPreview(PwItem item)
    {
        if (item.PreviewRequested || item.ModelName == null)
            return;
        item.PreviewRequested = true;
        if (ModelLibrary.IsSkipped(item.ModelName))
            return;

        var model = await Assets.Models.GetAsync(item.ModelName);
        if (model == null)
        {
            item.Status = "Could not be read";
            return;
        }
        if (!model.IsSelfContained)
        {
            item.Status = "Not rendered: references outside the file (" + string.Join(", ", model.MissingReferences.Take(3)) + ")";
            return;
        }
        try
        {
            item.Silhouette = model.BuildSilhouette(null, out _);
            item.CanPlace = true;
            item.Status = null;
            if (ReferenceEquals(SelectedItem, item))
            {
                OnPropertyChanged(nameof(HasPlaceableSelection));
                _editor.PlacementType = ElementType.Model;
            }
        }
        catch (Exception ex)
        {
            item.Status = "Not rendered: " + ex.Message;
        }
    }

    // ================= FOLDERS ================= //

    [RelayCommand]
    private async Task CreateFolder()
    {
        if (Context == PwContext.Objects)
        {
            await CreateObjectFileAsync();
            return;
        }
        if (!CanCreateFolder)
            return;
        string key = CacheKey;
        string? name = await AppServices.Dialogs.PromptAsync("New Folder", "Folder name:", Cache.UniqueFolderName(key, "New Folder"),
            text => string.IsNullOrWhiteSpace(text) ? "Enter a name." :
                Cache.GetFolders(key).Any(f => string.Equals(f.Name, text.Trim(), StringComparison.OrdinalIgnoreCase)) ? "A folder with this name already exists." :
                text.Trim() is "Unorganized" or "All" ? "This name is reserved." : null);
        if (name == null)
            return;
        Cache.GetFolders(key).Add(new CacheFolder { Name = name });
        await Cache.SaveAsync();
        await ReloadAsync();
        SelectedFolder = _allFolders.FirstOrDefault(f => f.Name == name);
    }

    private static readonly Regex FileNameChars = new("[^a-z0-9_]", RegexOptions.Compiled);

    /// <summary>"my cool object" → "my_cool_object.xml" (any name; shown as "My Cool Object").</summary>
    public static string MakeObjectFileName(string input)
    {
        string name = FileNameChars.Replace(input.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_'), string.Empty);
        if (name.EndsWith("_xml", StringComparison.Ordinal))
            name = name[..^4];
        while (name.Contains("__", StringComparison.Ordinal))
            name = name.Replace("__", "_");
        name = name.Trim('_');
        return name + ".xml";
    }

    private async Task CreateObjectFileAsync()
    {
        var existing = (await ObjectFilesAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string? input = await AppServices.Dialogs.PromptAsync("New Object XML File", "Name (e.g. \"my cool object\" is saved as my_cool_object.xml in xmlroot):", "new objects",
            text =>
            {
                if (string.IsNullOrWhiteSpace(text)) return "Enter a name.";
                string file = MakeObjectFileName(text);
                if (file == ".xml") return "Use letters or numbers.";
                if (string.Equals(file, "TriggerTemplates.xml", StringComparison.OrdinalIgnoreCase)) return "This name is reserved.";
                return existing.Contains(file) ? $"{file} already exists." : null;
            });
        if (input == null)
            return;
        string fileName = MakeObjectFileName(input);
        var doc = new XDocument(new XElement("Root", new XElement("Objects")));
        await AppServices.FileSystem.WriteAllBytesAsync(VPath.Combine(_editor.Project.XmlRoot, fileName), XmlHelpers.ToBytes(doc));
        Assets.Objects.Invalidate(fileName);
        await ReloadAsync();
        SelectedFolder = _allFolders.FirstOrDefault(f => f.FileName == fileName);
    }

    [RelayCommand]
    private async Task RenameFolder(PwFolder? folder)
    {
        if (folder == null || !folder.IsUserFolder)
            return;
        string key = CacheKey;
        string? name = await AppServices.Dialogs.PromptAsync("Rename Folder", "Folder name:", folder.Name,
            text => string.IsNullOrWhiteSpace(text) ? "Enter a name." :
                !string.Equals(text.Trim(), folder.Name, StringComparison.OrdinalIgnoreCase) &&
                Cache.GetFolders(key).Any(f => string.Equals(f.Name, text.Trim(), StringComparison.OrdinalIgnoreCase)) ? "A folder with this name already exists." : null);
        if (name == null)
            return;
        var cached = Cache.GetFolders(key).FirstOrDefault(f => f.Name == folder.Name);
        if (cached != null)
            cached.Name = name;
        await Cache.SaveAsync();
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task DeleteFolder(PwFolder? folder)
    {
        if (folder == null)
            return;
        await DeleteFoldersAsync(_selectedFolders.Contains(folder) ? _selectedFolders.ToList() : new List<PwFolder> { folder });
    }

    /// <summary>Delete key / menu on the left panel: Object XML files are deleted (after asking); cached folders are removed.</summary>
    public async Task DeleteFoldersAsync(IReadOnlyList<PwFolder> folders)
    {
        var files = folders.Where(f => f.FileName != null).Select(f => f.FileName!).ToList();
        var userFolders = folders.Where(f => f.IsUserFolder).ToList();
        try
        {
            if (files.Count > 0)
            {
                if (_editor.ObjectEdit is { } edit && files.Contains(edit.File, StringComparer.OrdinalIgnoreCase))
                {
                    await AppServices.Dialogs.ShowMessageAsync("Delete Object XML", $"{edit.File} is open in the Object tab. Return to the Scene tab first.", MessageKind.Info);
                    return;
                }
                string list = files.Count == 1 ? files[0] : $"{files.Count} files ({string.Join(", ", files.Take(4))}{(files.Count > 4 ? ", ..." : "")})";
                if (!await AppServices.Dialogs.ConfirmAsync("Delete Object XML",
                        $"Delete {list} from xmlroot?\n\nThis cannot be undone. Objects of {(files.Count == 1 ? "this file" : "these files")} used in levels will show as MISSING.",
                        "Delete", "Cancel", MessageKind.Warning))
                    return;
                foreach (var file in files)
                {
                    await AppServices.FileSystem.DeleteFileAsync(VPath.Combine(_editor.Project.XmlRoot, file));
                    Cache.Data.ObjectFileOrder.Remove(file);
                }
                await Cache.SaveAsync();
                await _editor.RefreshDefinitionsAsync(files);
                _editor.StatusText = $"Deleted {string.Join(", ", files)}";
                await ReloadAsync();
                return;
            }
            if (userFolders.Count == 0)
                return;
            string names = userFolders.Count == 1 ? $"the folder '{userFolders[0].Name}'" : $"{userFolders.Count} folders";
            if (!await AppServices.Dialogs.ConfirmAsync("Delete Folder", $"Delete {names}? Their items move to Unorganized; no files are deleted.", "Delete"))
                return;
            var removed = userFolders.Select(f => f.Name).ToHashSet();
            Cache.GetFolders(CacheKey).RemoveAll(f => removed.Contains(f.Name));
            await Cache.SaveAsync();
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync("Delete", ex.Message, MessageKind.Error);
        }
    }

    /// <summary>Folders that can be dragged to a new place: Object XML files, or user folders (Unorganized stays first).</summary>
    public bool CanReorder(PwFolder folder) => folder.FileName != null || folder.IsUserFolder;

    /// <summary>
    /// Drag-reordering of the left panel: <paramref name="moving"/> (in their current order) go before the folder at
    /// <paramref name="insertIndex"/> of <see cref="AllFolders"/> (Count = at the end).
    /// </summary>
    public async Task MoveFoldersAsync(IReadOnlyList<PwFolder> moving, int insertIndex)
    {
        var list = moving.Where(CanReorder).OrderBy(f => _allFolders.IndexOf(f)).ToList();
        if (list.Count == 0)
            return;
        insertIndex = Math.Clamp(insertIndex, 0, _allFolders.Count);
        var before = insertIndex < _allFolders.Count ? _allFolders[insertIndex] : null;
        var remaining = _allFolders.Where(f => !list.Contains(f)).ToList();
        // The folder the drop line is above, skipping the moved ones.
        while (before != null && list.Contains(before))
        {
            int i = _allFolders.IndexOf(before) + 1;
            before = i < _allFolders.Count ? _allFolders[i] : null;
        }
        int at = before == null ? remaining.Count : remaining.IndexOf(before);
        if (Context != PwContext.Objects)
        {
            // Unorganized / All always stays first.
            int firstUser = remaining.FindIndex(f => !f.IsUnorganized);
            at = Math.Max(at, firstUser < 0 ? remaining.Count : firstUser);
        }
        remaining.InsertRange(at, list);
        if (remaining.SequenceEqual(_allFolders))
            return;

        if (Context == PwContext.Objects)
        {
            Cache.Data.ObjectFileOrder = remaining.Where(f => f.FileName != null).Select(f => f.FileName!).ToList();
        }
        else
        {
            var cached = Cache.GetFolders(CacheKey);
            var byName = cached.ToDictionary(f => f.Name);
            var ordered = remaining.Where(f => f.IsUserFolder && byName.ContainsKey(f.Name)).Select(f => byName[f.Name]).ToList();
            ordered.AddRange(cached.Where(c => !ordered.Contains(c)));
            cached.Clear();
            cached.AddRange(ordered);
        }
        await Cache.SaveAsync();
        await ReloadAsync();
    }

    /// <summary>Single-folder reorder (kept for callers): <paramref name="folder"/> takes the place of <paramref name="target"/>.</summary>
    public Task MoveFolderAsync(PwFolder folder, PwFolder target)
    {
        int from = _allFolders.IndexOf(folder), to = _allFolders.IndexOf(target);
        return MoveFoldersAsync(new[] { folder }, from < to ? to + 1 : to);
    }

    /// <summary>Moves right-panel items into a cached folder (or back to Unorganized).</summary>
    public async Task AssignItemsAsync(IEnumerable<PwItem> items, PwFolder folder)
    {
        if (Context == PwContext.Objects || Context == PwContext.Items)
            return;
        string key = CacheKey;
        foreach (var item in items)
            Cache.AssignToFolder(key, item.Key, folder.IsUnorganized ? null : folder.Name);
        await Cache.SaveAsync();
        await ReloadAsync();
    }

    // ================= ITEMS ================= //

    [RelayCommand]
    private async Task RenameItem(PwItem? item)
    {
        if (item == null)
            return;
        try
        {
            switch (Context)
            {
                case PwContext.Textures when item.Texture != null:
                {
                    var entry = item.Texture;
                    string? name = await AppServices.Dialogs.PromptAsync("Rename Texture", "Texture name:", entry.Name,
                        text => string.IsNullOrWhiteSpace(text) ? "Enter a name." :
                            text.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || text.Contains('/') ? "The name contains invalid characters." : null);
                    if (name == null || name == entry.Name)
                        return;
                    string? folder = Cache.FolderOf(TexturesKey, entry.FileName);
                    await Textures.RenameAsync(entry, name);
                    if (folder != null)
                    {
                        Cache.AssignToFolder(TexturesKey, entry.FileName, null);
                        Cache.AssignToFolder(TexturesKey, name + entry.Extension, folder);
                        await Cache.SaveAsync();
                    }
                    _editor.StatusText = $"Renamed {entry.Name} to {name}. Levels using the old name are not changed.";
                    break;
                }
                case PwContext.Triggers when item.Trigger != null:
                {
                    string? name = await AppServices.Dialogs.PromptAsync("Rename Trigger", "Name:", item.Trigger.Name, t => string.IsNullOrWhiteSpace(t) ? "Enter a name." : null);
                    if (name == null) return;
                    item.Trigger.Name = name;
                    await Cache.SaveAsync();
                    break;
                }
                case PwContext.Areas when item.Area != null:
                {
                    string? name = await AppServices.Dialogs.PromptAsync("Rename Area", "Name:", item.Area.Name, t => string.IsNullOrWhiteSpace(t) ? "Enter a name." : null);
                    if (name == null) return;
                    item.Area.Name = name;
                    await Cache.SaveAsync();
                    break;
                }
                default:
                    return;
            }
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync("Rename", ex.Message, MessageKind.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteItem(PwItem? item)
    {
        if (item == null)
            return;
        await DeleteItemsAsync(_selectedItems.Contains(item) ? _selectedItems.ToList() : new List<PwItem> { item });
    }

    private static string ListNames(IReadOnlyList<string> names) =>
        string.Join(", ", names.Take(5)) + (names.Count > 5 ? $" and {names.Count - 5} more" : "");

    /// <summary>Deletes items of the current context (asks first).</summary>
    public async Task DeleteItemsAsync(IReadOnlyList<PwItem> items)
    {
        items = items.Where(CanDelete).ToList();
        if (items.Count == 0)
            return;
        try
        {
            switch (Context)
            {
                case PwContext.Textures:
                {
                    var deletable = items.Where(i => i.Texture != null && !TextureLibrary.IsProtected(i.Texture)).ToList();
                    if (deletable.Count == 0)
                    {
                        await AppServices.Dialogs.ShowMessageAsync("Delete Texture", "The selected textures are required by the game and cannot be deleted.", MessageKind.Warning);
                        return;
                    }
                    string kept = deletable.Count < items.Count ? $"\n({items.Count - deletable.Count} texture(s) required by the game are kept.)" : "";
                    if (!await AppServices.Dialogs.ConfirmAsync("Delete Texture",
                            $"Delete {ListNames(deletable.Select(d => d.Texture!.FileName).ToList())} from the project? Levels using them will show them as missing.{kept}",
                            "Delete", kind: MessageKind.Warning))
                        return;
                    foreach (var item in deletable)
                    {
                        await Textures.DeleteAsync(item.Texture!);
                        Cache.AssignToFolder(TexturesKey, item.Key, null);
                    }
                    break;
                }
                case PwContext.Triggers or PwContext.Areas:
                {
                    if (!await AppServices.Dialogs.ConfirmAsync(Context == PwContext.Triggers ? "Delete Trigger" : "Delete Area",
                            $"Remove {ListNames(items.Select(i => i.Label).ToList())} from the Project Window?", "Delete", kind: MessageKind.Warning))
                        return;
                    foreach (var item in items)
                    {
                        if (item.Trigger != null)
                        {
                            Cache.Data.Triggers.Remove(item.Trigger);
                            Cache.AssignToFolder(TriggersKey, item.Key, null);
                        }
                        if (item.Area != null)
                        {
                            Cache.Data.Areas.Remove(item.Area);
                            Cache.AssignToFolder(AreasKey, item.Key, null);
                        }
                    }
                    break;
                }
                case PwContext.Objects when SelectedFolder?.FileName is { } file:
                {
                    var names = items.Where(i => i.Definition != null).Select(i => i.Definition!.Name).ToList();
                    if (_editor.ObjectEdit is { } edit && string.Equals(edit.File, file, StringComparison.OrdinalIgnoreCase) && names.Contains(edit.Name))
                    {
                        await AppServices.Dialogs.ShowMessageAsync("Delete Object", $"'{edit.Name}' is open in the Object tab. Return to the Scene tab first.", MessageKind.Info);
                        return;
                    }
                    if (!await AppServices.Dialogs.ConfirmAsync("Delete Object",
                            $"Remove {ListNames(names)} from {file}?\n\nLevels that use {(names.Count == 1 ? "it" : "them")} will show {(names.Count == 1 ? "it" : "them")} as MISSING.",
                            "Delete", kind: MessageKind.Warning))
                        return;
                    await RemoveDefinitionsAsync(file, names);
                    await _editor.RefreshDefinitionsAsync(new[] { file });
                    _editor.StatusText = $"Removed {ListNames(names)} from {file}";
                    break;
                }
                case PwContext.Models:
                {
                    var names = items.Where(i => i.ModelName != null).Select(i => i.ModelName!).ToList();
                    if (!await AppServices.Dialogs.ConfirmAsync("Delete Model",
                            $"Delete {ListNames(names.Select(n => n + ".xml").ToList())} (and the model folder of the same name, if any) from the project? Levels using them will show them as missing.",
                            "Delete", kind: MessageKind.Warning))
                        return;
                    foreach (var name in names)
                    {
                        string xml = VPath.Combine(_editor.Project.ModelsFolder, name + ".xml");
                        if (await AppServices.FileSystem.FileExistsAsync(xml))
                            await AppServices.FileSystem.DeleteFileAsync(xml);
                        string folder = VPath.Combine(_editor.Project.ModelsFolder, name);
                        if (await AppServices.FileSystem.DirectoryExistsAsync(folder))
                            await AppServices.FileSystem.DeleteDirectoryAsync(folder);
                        Assets.Models.Invalidate(name);
                        Cache.AssignToFolder(ModelsKey, name, null);
                    }
                    _editor.StatusText = $"Deleted {ListNames(names)}";
                    break;
                }
                default:
                    return;
            }
            await Cache.SaveAsync();
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync("Delete", ex.Message, MessageKind.Error);
        }
    }

    // ================= IMPORT (files from the OS) ================= //

    /// <summary>"Import Textures..." / "Import Models...": picks files and folders together, then imports them.</summary>
    [RelayCommand]
    public async Task Import()
    {
        if (!CanImport)
            return;
        bool models = Context == PwContext.Models;
        var dialog = models
            ? new ViewModels.Dialogs.ImportDialogViewModel("Import Models",
                "Model XML files, with their folder (a folder of the same name). Folders holding model XML files import everything in them.",
                "Model XML", new[] { "*.xml" })
            : new ViewModels.Dialogs.ImportDialogViewModel("Import Textures",
                "Images (with their .plist / .json for animated textures). Folders are imported recursively and become folders here.",
                "Textures", new[] { "*.png", "*.jpg", "*.jpeg", "*.plist", "*.json" });
        if (await AppServices.Dialogs.ShowAsync(dialog) is not IReadOnlyList<IStorageItem> picked || picked.Count == 0)
            return;
        if (models)
            await ImportModelsAsync(picked);
        else
            await ImportFilesAsync(picked);
    }

    /// <summary>
    /// Imports model XML files into models/. A picked folder holding model XML files is imported with everything in
    /// it (its XML files and their folders); any other picked folder is a model's folder and is copied as is.
    /// </summary>
    public async Task ImportModelsAsync(IReadOnlyList<IStorageItem> picked)
    {
        var xmls = new List<IStorageFile>();
        var folders = new List<IStorageFolder>();
        foreach (var item in picked)
        {
            if (item is IStorageFile file && VPath.HasExtension(file.Name, ".xml"))
                xmls.Add(file);
            else if (item is IStorageFolder folder)
            {
                var content = new List<IStorageItem>();
                await foreach (var child in folder.GetItemsAsync())
                    content.Add(child);
                var inner = content.OfType<IStorageFile>().Where(f => VPath.HasExtension(f.Name, ".xml")).ToList();
                if (inner.Count > 0)
                {
                    xmls.AddRange(inner);
                    folders.AddRange(content.OfType<IStorageFolder>());
                }
                else
                {
                    folders.Add(folder);
                }
            }
        }
        if (xmls.Count == 0 && folders.Count == 0)
        {
            await AppServices.Dialogs.ShowMessageAsync("Import Models", "No model XML file or folder was selected.", MessageKind.Info);
            return;
        }
        try
        {
            IsBusy = true;
            string root = _editor.Project.ModelsFolder;
            await AppServices.FileSystem.CreateDirectoryAsync(root);
            var imported = new List<string>();
            var replace = new ReplaceAsker("Import Models");
            foreach (var file in xmls)
            {
                string target = VPath.Combine(root, file.Name);
                if (await AppServices.FileSystem.FileExistsAsync(target) &&
                    !await replace.AskAsync($"{file.Name} already exists. Replace it?"))
                    continue;
                await StorageImport.CopyFileAsync(AppServices.FileSystem, file, target);
                imported.Add(VPath.GetFileNameWithoutExtension(file.Name));
            }
            int copiedFolders = 0;
            foreach (var folder in folders)
            {
                string target = VPath.Combine(root, folder.Name);
                if (await AppServices.FileSystem.DirectoryExistsAsync(target))
                {
                    if (!await replace.AskAsync($"The folder {folder.Name} already exists in models. Replace it?"))
                        continue;
                    await AppServices.FileSystem.DeleteDirectoryAsync(target);
                }
                await StorageImport.CopyFolderAsync(AppServices.FileSystem, folder, target);
                copiedFolders++;
            }
            Assets.Models.Invalidate();
            if (SelectedFolder is { IsUserFolder: true } selected)
                foreach (var name in imported)
                    Cache.AssignToFolder(ModelsKey, name, selected.Name);
            await Cache.SaveAsync();
            _editor.StatusText = $"Imported {imported.Count} model(s) and {copiedFolders} folder(s)";
            await ReloadAsync();
            await RevealNewAsync(imported);
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync("Import Models", ex.Message, MessageKind.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ImportFilesAsync(IReadOnlyList<IStorageItem> dropped)
    {
        if (Context == PwContext.Models)
        {
            await ImportModelsAsync(dropped);
            return;
        }
        if (Context != PwContext.Textures)
            return;
        try
        {
            IsBusy = true;
            var files = await TextureImport.CollectAsync(dropped);
            files = files.Where(f => TextureLibrary.IsImageFile(f.File.Name) || VPath.HasExtension(f.File.Name, ".plist", ".json")).ToList();
            if (files.Count == 0)
                return;

            var replaceTexture = new ReplaceAsker("Import");
            var imported = await Textures.ImportAsync(
                files.Select(f => (f.File.Name, (Func<Task<byte[]>>)(() => StorageImport.ReadAllBytesAsync(f.File)))).ToList(),
                name => replaceTexture.AskAsync($"{name} already exists. Replace it?"));

            // Dropped folders become virtual folders (recursively, "Name (1)" when the name is taken).
            var folderNames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (file, folder) in files)
            {
                if (string.IsNullOrEmpty(folder) || !TextureLibrary.IsImageFile(file.Name))
                    continue;
                string stem = VPath.GetFileNameWithoutExtension(file.Name);
                if (!imported.Contains(stem))
                    continue;
                if (!folderNames.TryGetValue(folder, out var virtualName))
                {
                    virtualName = Cache.UniqueFolderName(TexturesKey, folder.Replace('/', ' ').Trim());
                    Cache.GetFolders(TexturesKey).Add(new CacheFolder { Name = virtualName });
                    folderNames[folder] = virtualName;
                }
                var entry = Textures.Find(stem);
                Cache.AssignToFolder(TexturesKey, entry?.FileName ?? file.Name, virtualName);
            }

            // Loose files go into the selected folder.
            if (SelectedFolder is { IsUserFolder: true } selected)
            {
                foreach (var (file, folder) in files.Where(f => string.IsNullOrEmpty(f.Folder) && TextureLibrary.IsImageFile(f.File.Name)))
                {
                    var entry = Textures.Find(VPath.GetFileNameWithoutExtension(file.Name));
                    if (entry != null)
                        Cache.AssignToFolder(TexturesKey, entry.FileName, selected.Name);
                }
            }

            await Cache.SaveAsync();
            _editor.StatusText = $"Imported {imported.Count} texture(s)";
            await ReloadAsync();
            await RevealNewAsync(imported.Select(n => Textures.Find(n)?.FileName).OfType<string>().ToList());
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync("Import", ex.Message, MessageKind.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ================= SAVING ELEMENTS FROM THE HIERARCHY ================= //

    public bool CanAcceptElements(IReadOnlyList<SceneElement> elements) => Context switch
    {
        PwContext.Objects => SelectedFolder?.FileName != null && elements.Count > 0,
        PwContext.Triggers => elements.Count == 1 && elements[0] is TriggerElement,
        PwContext.Areas => elements.Count == 1 && elements[0] is AreaElement,
        _ => false,
    };

    public async Task AcceptElementsAsync(IReadOnlyList<SceneElement> elements)
    {
        try
        {
            switch (Context)
            {
                case PwContext.Objects when SelectedFolder?.FileName is { } file:
                {
                    var saved = await SaveDefinitionsAsync(file, elements);
                    // A saved Object without definition becomes an instance of its new definition (as do others of that name).
                    foreach (var (element, name) in saved)
                        if (element is ObjectElement { IsReference: false } obj && obj.Name != name)
                            obj.Name = name;
                    await _editor.RefreshDefinitionsAsync(new[] { file });
                    await LoadItemsAsync();
                    await RevealNewAsync(saved.Select(x => x.Name).ToList(), file);
                    return;
                }
                case PwContext.Triggers when elements.Count == 1 && elements[0] is TriggerElement trigger:
                {
                    var stored = new StoredTrigger { Name = trigger.Name, Width = trigger.Width, Height = trigger.Height, Content = trigger.ContentXml };
                    Cache.Data.Triggers.Add(stored);
                    if (SelectedFolder is { IsUserFolder: true } folder)
                        Cache.AssignToFolder(TriggersKey, stored.Id, folder.Name);
                    await Cache.SaveAsync();
                    _editor.StatusText = $"Stored Trigger '{trigger.Name}'";
                    await RevealNewAsync(new[] { stored.Id });
                    return;
                }
                case PwContext.Areas when elements.Count == 1 && elements[0] is AreaElement area:
                {
                    var stored = new StoredArea
                    {
                        Name = area.Name, Width = area.Width, Height = area.Height, AreaType = area.AreaType, Distance = area.Distance,
                        ItemName = area.ItemName, Score = area.Score, Key = area.Key, Description = area.Description,
                    };
                    Cache.Data.Areas.Add(stored);
                    if (SelectedFolder is { IsUserFolder: true } folder)
                        Cache.AssignToFolder(AreasKey, stored.Id, folder.Name);
                    await Cache.SaveAsync();
                    _editor.StatusText = $"Stored Area '{area.Name}'";
                    await RevealNewAsync(new[] { stored.Id });
                    return;
                }
                case PwContext.Triggers or PwContext.Areas:
                    await AppServices.Dialogs.ShowMessageAsync("Project Window", $"Drag exactly one {(Context == PwContext.Triggers ? "Trigger" : "Area")} here.", MessageKind.Info);
                    return;
                default:
                    return;
            }
            await LoadItemsAsync();
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync("Save to Project", ex.Message, MessageKind.Error);
        }
    }

    private async Task<XDocument> ReadXmlRootFileAsync(string path)
    {
        if (!await AppServices.FileSystem.FileExistsAsync(path))
            return new XDocument(new XElement("Root", new XElement("Objects")));
        return XmlHelpers.ParseDocument(await AppServices.FileSystem.ReadAllBytesAsync(path));
    }

    /// <summary>
    /// Saves elements as object definitions. Objects are saved as they are; other elements get an Object
    /// parent. Names already used in the file get a "_1" suffix.
    /// </summary>
    private async Task<List<(SceneElement Element, string Name)>> SaveDefinitionsAsync(string file, IReadOnlyList<SceneElement> elements)
    {
        string path = VPath.Combine(_editor.Project.XmlRoot, file);
        var doc = await ReadXmlRootFileAsync(path);
        var root = doc.Root ?? new XElement("Root");
        var objects = root.Element("Objects");
        if (objects == null)
        {
            objects = new XElement("Objects");
            root.Add(objects);
        }
        var used = objects.Elements("Object").Select(o => (string?)o.Attribute("Name")).Where(n => n != null).Select(n => n!).ToHashSet(StringComparer.Ordinal);

        var writer = new SceneXmlWriter
        {
            TextureSizeProvider = name => Textures.TryGet(name) is { } a ? (a.Width, a.Height) : null,
        };

        var saved = new List<string>();
        var result = new List<(SceneElement, string)>();
        foreach (var element in elements)
        {
            string baseName = string.IsNullOrEmpty(element.Name) ? element.Type.ToString() : element.Name;
            string name = baseName;
            for (int i = 1; used.Contains(name); i++)
                name = $"{baseName}_{i}";
            used.Add(name);

            XElement definition;
            if (element is ObjectElement obj)
            {
                var copy = (ObjectElement)obj.Clone(keepIds: false);
                copy.Name = name;
                copy.X = 0;
                copy.Y = 0;
                copy.PositionWasPresent = false;
                definition = (XElement)writer.WriteElement(copy)!;
                // A definition keeps the layer-independent content; a referenced prefab keeps its own content.
                if (obj.Children.Count == 0 && obj.Reference != null && definition.Element("Content") == null)
                {
                    var content = new XElement("Content");
                    var reference = new XElement("Object", new XAttribute("Name", obj.Reference.Name));
                    content.Add(reference);
                    definition.Add(content);
                }
            }
            else
            {
                var copy = element.Clone(keepIds: false);
                // The content's origin is the element itself.
                copy.X = 0;
                copy.Y = 0;
                definition = writer.WriteDefinition(name, new[] { copy });
            }
            objects.Add(definition);
            saved.Add(name);
            result.Add((element, name));
        }

        await AppServices.FileSystem.WriteAllBytesAsync(path, XmlHelpers.ToBytes(doc));
        Assets.Objects.Invalidate(file);
        _editor.StatusText = $"Saved {string.Join(", ", saved)} to {file}";
        if (writer.Errors.Count > 0)
            await AppServices.Dialogs.ShowMessageAsync("Save to Project", string.Join("\n", writer.Errors), MessageKind.Warning);
        return result;
    }

    private async Task RemoveDefinitionsAsync(string file, IReadOnlyCollection<string> names)
    {
        string path = VPath.Combine(_editor.Project.XmlRoot, file);
        var doc = await ReadXmlRootFileAsync(path);
        foreach (var xml in doc.Root?.Element("Objects")?.Elements("Object").Where(o => names.Contains((string?)o.Attribute("Name") ?? string.Empty)).ToList()
                     ?? new List<XElement>())
            xml.Remove();
        await AppServices.FileSystem.WriteAllBytesAsync(path, XmlHelpers.ToBytes(doc));
        Assets.Objects.Invalidate(file);
    }

    // ================= PLATFORM SURFACES ================= //

    private readonly Dictionary<PlatformSurfaceType, ObjectDefinition> _platformPreviews = new();

    /// <summary>A 500 × 500 platform of the surface type, for the grid and the placement preview.</summary>
    private async Task<ObjectDefinition> PlatformPreviewAsync(PlatformSurfaceType surface)
    {
        if (_platformPreviews.TryGetValue(surface, out var cached))
            return cached;
        var group = await PlatformSurface.BuildAsync(Textures, surface, new Rect(0, 0, 500, 500), 12345);
        var def = new ObjectDefinition(surface.ToString(), string.Empty, new XElement("Object"))
        {
            Content = group.Children.ToList(),
            IsBuilt = true,
        };
        def.ComputeBounds();
        _platformPreviews[surface] = def;
        return def;
    }

    // ================= OBJECT FILES: TYPE, RENAME, EDIT ================= //

    private async Task DetectFileTypesAsync(IEnumerable<PwFolder> folders)
    {
        foreach (var folder in folders.Where(f => f.FileName != null).ToList())
        {
            try { folder.IsBuilding = await Assets.Objects.IsBuildingFileAsync(folder.FileName!); }
            catch { folder.IsBuilding = false; }
        }
    }

    /// <summary>
    /// Object ↔ Building: Building writes In / Out (and Box) on every definition of the file, computed from its
    /// Platforms; Object removes them.
    /// </summary>
    public async Task SetFileTypeAsync(PwFolder folder, bool building)
    {
        if (folder.FileName is not { } file || folder.IsBuilding == building)
            return;
        try
        {
            string path = VPath.Combine(_editor.Project.XmlRoot, file);
            var doc = await ObjectFileOps.ReadAsync(path);
            var definitions = await Assets.Objects.GetFileDefinitionsAsync(file);
            foreach (var xml in doc.Root?.Element("Objects")?.Elements("Object") ?? Enumerable.Empty<XElement>())
            {
                if (!building)
                {
                    ObjectFileOps.RemoveBuildingAttributes(xml);
                    continue;
                }
                var def = definitions.FirstOrDefault(d => d.Name == (string?)xml.Attribute("Name"));
                if (def == null)
                    continue;
                _editor.BuildDefinition(def);
                var (inPt, outPt, box) = ObjectFileOps.ComputeBuilding(def.Content, def.In, def.Out);
                ObjectFileOps.WriteBuildingAttributes(xml, inPt, outPt, box);
            }
            await ObjectFileOps.WriteAsync(path, doc);
            Assets.Objects.Invalidate(file);
            folder.IsBuilding = building;
            _editor.StatusText = $"{file} is now {(building ? "a Building" : "an Object")} file";
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync("Object XML Type", ex.Message, MessageKind.Error);
        }
    }

    /// <summary>Double click / Edit Object on a definition: opens it in the Object tab.</summary>
    public async Task EditObjectAsync(PwItem item)
    {
        if (item.PlatformSurface == null && item.Definition is { } def)
            await _editor.EnterObjectEditAsync(def);
    }

    /// <summary>
    /// Renames a definition and every reference to it in the project: its file, the Object XML files and the levels
    /// whose Sets list that file. Asks first; an existing name is refused.
    /// </summary>
    public async Task RenameDefinitionAsync(PwItem item)
    {
        if (item.Definition is not { } def)
            return;
        var allNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in await ObjectFilesAsync())
            foreach (var d in await Assets.Objects.GetFileDefinitionsAsync(file))
                allNames.Add(d.Name);

        string? name = await AppServices.Dialogs.PromptAsync("Rename Object", $"New name for '{def.Name}' ({def.SourceFile}):", def.Name,
            text => string.IsNullOrWhiteSpace(text) ? "Enter a name." : null);
        if (name == null || (name = name.Trim()) == def.Name)
            return;
        if (allNames.Contains(name))
        {
            await AppServices.Dialogs.ShowMessageAsync("Rename Object", $"An object named '{name}' already exists. The name was not changed.", MessageKind.Warning);
            return;
        }
        if (!await AppServices.Dialogs.ConfirmAsync("Rename Object",
                $"Rename '{def.Name}' to '{name}'?\n\nEvery reference in the Object XML files and levels that use {def.SourceFile} is updated too. This can take a moment.",
                "Rename", "Cancel", MessageKind.Question))
            return;
        try
        {
            IsBusy = true;
            string old = def.Name;
            var report = await ObjectFileOps.RenameDefinitionAsync(_editor.Project, def.SourceFile, old, name);
            Assets.Objects.Invalidate();
            await _editor.OnDefinitionRenamedAsync(old, name);
            await ReloadAsync();
            _editor.StatusText = $"Renamed {old} to {name} in {report.ChangedFiles.Count} file(s)";
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync("Rename Object", ex.Message, MessageKind.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ================= KEYBOARD/SELECTION HELPERS ================= //

    public void Select(PwItem? item) => SelectedItem = item;

    /// <summary>Textures, stored Triggers and Areas, object definitions and model XML files can be deleted.</summary>
    public bool CanDelete(PwItem item) =>
        item.Texture != null || item.Trigger != null || item.Area != null || item.ModelName != null || (item.Definition != null && item.PlatformSurface == null);

    /// <summary>Delete key in the Project Window: the selected files (left panel) or the selected items (right panel).</summary>
    public async Task DeleteSelectionAsync()
    {
        if (LastClickInFolders)
        {
            if (_selectedFolders.Count > 0)
                await DeleteFoldersAsync(_selectedFolders.ToList());
            return;
        }
        var items = _selectedItems.Where(CanDelete).ToList();
        if (items.Count > 0)
            await DeleteItemsAsync(items);
    }
}
