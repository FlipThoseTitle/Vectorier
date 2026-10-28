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
using Vectorier_Editor_Avalonia.Models.Scene;
using Vectorier_Editor_Avalonia.Services;
using Vectorier_Editor_Avalonia.Services.Assets;
using Vectorier_Editor_Avalonia.Services.IO;
using Vectorier_Editor_Avalonia.Services.Projects;
using Vectorier_Editor_Avalonia.Services.Xml;
using Vectorier_Editor_Avalonia.ViewModels.ProjectManager;

namespace Vectorier_Editor_Avalonia.ViewModels.Editor;

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

    public ObservableCollection<PwFolder> Folders { get; } = new();
    public ObservableCollection<PwItem> Items { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsFolders), nameof(ShowsItems), nameof(CanCreateFolder), nameof(IsTextureContext),
        nameof(IsObjectContext), nameof(IsModelContext), nameof(IsItemContext), nameof(IsStoredContext), nameof(EmptyText), nameof(AcceptsFileDrop))]
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
    public bool AcceptsFileDrop => Context == PwContext.Textures;

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
        PwContext.Objects => SelectedFolder == null ? "Select an XML file on the left." : "This file has no objects. Drag elements from the Hierarchy here to save them.",
        PwContext.Textures => "No textures here. Drag images or folders from your file explorer to import them.",
        PwContext.Triggers => "Drag a Trigger from the Hierarchy here to keep it for later. Click in the Scene to add a generic Trigger.",
        PwContext.Areas => "Drag an Area from the Hierarchy here to keep it for later. Click in the Scene to add a generic Area.",
        PwContext.Models => "No model files in this folder.",
        _ => string.Empty,
    };

    /// <summary>Element type the Project Window currently lists assets for.</summary>
    public ElementType ElementType => _elementType;

    public bool HasPlaceableSelection => SelectedItem is { CanPlace: true };

    public string? SelectedTextureName => Context == PwContext.Textures ? SelectedItem?.Texture?.Name : null;

    public string? SelectedModelName => Context == PwContext.Models && SelectedItem is { CanPlace: true } item ? item.ModelName : null;

    /// <summary>What a click in the Scene creates from the Project Window selection.</summary>
    public ElementSource? CurrentSource => SourceFor(SelectedItem);

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

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedFolderChanged(PwFolder? oldValue, PwFolder? newValue)
    {
        if (oldValue != null) oldValue.IsSelected = false;
        if (newValue != null) newValue.IsSelected = true;
        _ = LoadItemsAsync();
    }

    partial void OnSelectedItemChanged(PwItem? oldValue, PwItem? newValue)
    {
        if (oldValue != null) oldValue.IsSelected = false;
        if (newValue != null) newValue.IsSelected = true;
        OnPropertyChanged(nameof(HasPlaceableSelection));

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
        SelectedItem = null;
        Items.Clear();
        _allItems.Clear();
        Folders.Clear();
        SelectedFolder = null;

        try
        {
            IsBusy = true;
            await Cache.EnsureLoadedAsync();
            var folders = await BuildFoldersAsync();
            if (version != _reloadVersion)
                return;
            foreach (var folder in folders)
                Folders.Add(folder);
            SelectedFolder = Folders.FirstOrDefault(f => f.Name == previousFolder) ?? Folders.FirstOrDefault();
            if (SelectedFolder == null)
                await LoadItemsAsync();
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
        var items = new List<PwItem>();

        try
        {
            switch (Context)
            {
                case PwContext.Objects when folder?.FileName != null:
                {
                    var table = await Assets.Objects.ResolveForFileAsync(folder.FileName);
                    var reader = new SceneXmlReader(table);
                    foreach (var def in await Assets.Objects.GetFileDefinitionsAsync(folder.FileName))
                    {
                        reader.BuildDefinition(def);
                        items.Add(new PwItem(def.Name, def.Name) { Definition = def });
                    }
                    break;
                }
                case PwContext.Textures:
                    if (!Textures.IsIndexed)
                        await Textures.RefreshIndexAsync();
                    items.AddRange(InFolder(TextureItems(), folder));
                    break;
                case PwContext.Triggers:
                    items.AddRange(InFolder(Cache.Data.Triggers.Select(t => new PwItem(t.Id, t.Name) { Trigger = t }), folder));
                    break;
                case PwContext.Areas:
                    items.AddRange(InFolder(Cache.Data.Areas.Select(a => new PwItem(a.Id, a.Name) { Area = a }), folder));
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
                    items.AddRange(InFolder(all, folder));
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
        Items.Clear();
        string search = SearchText.Trim();
        foreach (var item in _allItems)
        {
            if (search.Length == 0 || item.Label.Contains(search, StringComparison.OrdinalIgnoreCase))
                Items.Add(item);
        }
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
        SelectedFolder = Folders.FirstOrDefault(f => f.Name == name);
    }

    private static readonly Regex FileNameChars = new("[^a-z0-9_]", RegexOptions.Compiled);

    /// <summary>"Meow" → "objects_meow.xml"; names that already start with objects/buildings keep their prefix.</summary>
    public static string MakeObjectFileName(string input)
    {
        string name = FileNameChars.Replace(input.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_'), string.Empty);
        if (name.EndsWith("_xml", StringComparison.Ordinal))
            name = name[..^4];
        while (name.Contains("__", StringComparison.Ordinal))
            name = name.Replace("__", "_");
        name = name.Trim('_');
        if (!name.StartsWith("objects", StringComparison.Ordinal) && !name.StartsWith("buildings", StringComparison.Ordinal))
            name = "objects_" + name;
        return name.TrimEnd('_') + ".xml";
    }

    private async Task CreateObjectFileAsync()
    {
        var existing = (await ObjectFilesAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string? input = await AppServices.Dialogs.PromptAsync("New Objects File", "Name (saved as objects_name.xml in xmlroot):", "new",
            text =>
            {
                if (string.IsNullOrWhiteSpace(text)) return "Enter a name.";
                string file = MakeObjectFileName(text);
                if (file is "objects_.xml" or ".xml") return "Enter a name.";
                return existing.Contains(file) ? $"{file} already exists." : null;
            });
        if (input == null)
            return;
        string fileName = MakeObjectFileName(input);
        var doc = new XDocument(new XElement("Root", new XElement("Objects")));
        await AppServices.FileSystem.WriteAllBytesAsync(VPath.Combine(_editor.Project.XmlRoot, fileName), XmlHelpers.ToBytes(doc));
        Assets.Objects.Invalidate(fileName);
        await ReloadAsync();
        SelectedFolder = Folders.FirstOrDefault(f => f.FileName == fileName);
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
        if (folder.IsFile)
        {
            await AppServices.Dialogs.ShowMessageAsync("Delete", "Object files are deleted from the Project Manager (XML Files page).", MessageKind.Info);
            return;
        }
        if (!folder.IsUserFolder)
            return;
        if (!await AppServices.Dialogs.ConfirmAsync("Delete Folder", $"Delete the folder '{folder.Name}'? Its items move to Unorganized; no files are deleted.", "Delete"))
            return;
        Cache.GetFolders(CacheKey).RemoveAll(f => f.Name == folder.Name);
        await Cache.SaveAsync();
        await ReloadAsync();
    }

    /// <summary>Drag-reordering of the left panel.</summary>
    public async Task MoveFolderAsync(PwFolder folder, PwFolder target)
    {
        if (ReferenceEquals(folder, target))
            return;
        if (Context == PwContext.Objects && folder.FileName != null && target.FileName != null)
        {
            var order = Folders.Where(f => f.FileName != null).Select(f => f.FileName!).ToList();
            order.Remove(folder.FileName);
            order.Insert(Math.Max(0, order.IndexOf(target.FileName) + (Folders.IndexOf(folder) < Folders.IndexOf(target) ? 1 : 0)), folder.FileName);
            Cache.Data.ObjectFileOrder = order;
        }
        else if (folder.IsUserFolder && target.IsUserFolder)
        {
            var list = Cache.GetFolders(CacheKey);
            var item = list.FirstOrDefault(f => f.Name == folder.Name);
            var targetItem = list.FirstOrDefault(f => f.Name == target.Name);
            if (item == null || targetItem == null)
                return;
            int from = list.IndexOf(item);
            int to = list.IndexOf(targetItem);
            list.RemoveAt(from);
            list.Insert(to, item);
        }
        else
        {
            return;
        }
        await Cache.SaveAsync();
        await ReloadAsync();
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
        try
        {
            switch (Context)
            {
                case PwContext.Textures when item.Texture != null:
                    if (TextureLibrary.IsProtected(item.Texture))
                    {
                        await AppServices.Dialogs.ShowMessageAsync("Delete Texture", $"{item.Label} is required by the game and cannot be deleted.", MessageKind.Warning);
                        return;
                    }
                    if (!await AppServices.Dialogs.ConfirmAsync("Delete Texture", $"Delete {item.Texture.FileName} from the project? Levels using it will show it as missing.", "Delete", kind: MessageKind.Warning))
                        return;
                    await Textures.DeleteAsync(item.Texture);
                    Cache.AssignToFolder(TexturesKey, item.Key, null);
                    break;
                case PwContext.Triggers when item.Trigger != null:
                    Cache.Data.Triggers.Remove(item.Trigger);
                    Cache.AssignToFolder(TriggersKey, item.Key, null);
                    break;
                case PwContext.Areas when item.Area != null:
                    Cache.Data.Areas.Remove(item.Area);
                    Cache.AssignToFolder(AreasKey, item.Key, null);
                    break;
                case PwContext.Objects when item.Definition != null && SelectedFolder?.FileName is { } file:
                    if (!await AppServices.Dialogs.ConfirmAsync("Delete Object", $"Remove '{item.Definition.Name}' from {file}?", "Delete", kind: MessageKind.Warning))
                        return;
                    await RemoveDefinitionAsync(file, item.Definition.Name);
                    break;
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

    public async Task ImportFilesAsync(IReadOnlyList<IStorageItem> dropped)
    {
        if (Context != PwContext.Textures)
            return;
        try
        {
            IsBusy = true;
            var files = await TextureImport.CollectAsync(dropped);
            files = files.Where(f => TextureLibrary.IsImageFile(f.File.Name) || VPath.HasExtension(f.File.Name, ".plist", ".json")).ToList();
            if (files.Count == 0)
                return;

            var imported = await Textures.ImportAsync(
                files.Select(f => (f.File.Name, (Func<Task<byte[]>>)(() => StorageImport.ReadAllBytesAsync(f.File)))).ToList(),
                name => AppServices.Dialogs.ConfirmAsync("Import", $"{name} already exists. Replace it?", "Replace", "Skip"));

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
                    await SaveDefinitionsAsync(file, elements);
                    break;
                case PwContext.Triggers when elements.Count == 1 && elements[0] is TriggerElement trigger:
                {
                    var stored = new StoredTrigger { Name = trigger.Name, Width = trigger.Width, Height = trigger.Height, Content = trigger.ContentXml };
                    Cache.Data.Triggers.Add(stored);
                    if (SelectedFolder is { IsUserFolder: true } folder)
                        Cache.AssignToFolder(TriggersKey, stored.Id, folder.Name);
                    await Cache.SaveAsync();
                    _editor.StatusText = $"Stored Trigger '{trigger.Name}'";
                    break;
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
                    break;
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
    private async Task SaveDefinitionsAsync(string file, IReadOnlyList<SceneElement> elements)
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
        }

        await AppServices.FileSystem.WriteAllBytesAsync(path, XmlHelpers.ToBytes(doc));
        Assets.Objects.Invalidate(file);
        _editor.StatusText = $"Saved {string.Join(", ", saved)} to {file}";
        if (writer.Errors.Count > 0)
            await AppServices.Dialogs.ShowMessageAsync("Save to Project", string.Join("\n", writer.Errors), MessageKind.Warning);
    }

    private async Task RemoveDefinitionAsync(string file, string name)
    {
        string path = VPath.Combine(_editor.Project.XmlRoot, file);
        var doc = await ReadXmlRootFileAsync(path);
        doc.Root?.Element("Objects")?.Elements("Object").FirstOrDefault(o => (string?)o.Attribute("Name") == name)?.Remove();
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

    /// <summary>Textures, stored Triggers and Areas can be deleted; objects are renamed instead.</summary>
    public bool CanDelete(PwItem item) => item.Texture != null || item.Trigger != null || item.Area != null;
}
