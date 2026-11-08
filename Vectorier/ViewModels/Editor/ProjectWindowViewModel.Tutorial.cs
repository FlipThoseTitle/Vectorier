using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Vectorier.Models.Scene;
using Vectorier.Services.Localization;

namespace Vectorier.ViewModels.Editor;

public partial class ProjectWindowViewModel
{
    private Func<PwFolder, bool>? _tutorialFolders;
    private Func<PwItem, bool>? _tutorialItems;

    /// <summary>The tutorial shows only some folders and items (null: everything again).</summary>
    public void SetTutorialFilter(Func<PwFolder, bool>? folders, Func<PwItem, bool>? items)
    {
        _tutorialFolders = folders;
        _tutorialItems = items;
        ApplyFolderFilter();
        if (SelectedFolder != null && !Folders.Contains(SelectedFolder) && Folders.Count > 0)
            SelectedFolder = Folders[0];
        ApplyFilter();
    }

    /// <summary>
    /// True for the Textures folder the named texture is filed in (Unorganized when it is in none), wherever the user
    /// organised it: the tutorial shows only that folder.
    /// </summary>
    public bool FolderHoldsTexture(PwFolder folder, string textureName)
    {
        foreach (var item in TextureItems())
        {
            if (!string.Equals(item.Texture?.Name, textureName, StringComparison.OrdinalIgnoreCase))
                continue;
            string? name = Cache.FolderOf(TexturesKey, item.Key);
            if (folder.IsUnorganized ? name == null : name == folder.Name)
                return true;
        }
        return false;
    }

    private void ApplyFolderFilter()
    {
        string search = FolderSearchText.Trim();
        var source = _tutorialFolders == null ? _allFolders : _allFolders.Where(_tutorialFolders).ToList();
        var shown = search.Length == 0 ? source : source.Where(f => f.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
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
    [ObservableProperty] private string _header = Loc.T("Project");
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

    public string ImportLabel => Context == PwContext.Models ? Loc.T("Import Models...") : Loc.T("Import Textures...");

    public string EmptyText => Context switch
    {
        PwContext.None => ElementType switch
        {
            ElementType.Platform => Loc.T("Click in the Scene to add a 100 × 100 Platform, or drag to size it."),
            ElementType.Trapezoid => Loc.T("Click in the Scene to add a Trapezoid. Resizing keeps the 26.565° slope; hold Ctrl to size freely."),
            ElementType.Camera => Loc.T("Click in the Scene to add a Camera (one per game mode)."),
            ElementType.Spawn => Loc.T("Click in the Scene to add a Spawn."),
            ElementType.Comment => Loc.T("Click in the Scene to add a Comment, or drag to size it. Its text is its name."),
            _ => string.Empty,
        },
        PwContext.Objects => HasMultipleFolders ? Loc.T("Several files are selected: drag them to reorder, or press Delete to remove them.")
            : SelectedFolder == null ? Loc.T("Select an XML file on the left.") : Loc.T("This file has no objects. Drag elements from the Hierarchy here to save them."),
        PwContext.Textures => Loc.T("No textures here. Drag images or folders from your file explorer to import them."),
        PwContext.Triggers => Loc.T("Drag a Trigger from the Hierarchy here to keep it for later. Click in the Scene to add a generic Trigger."),
        PwContext.Areas => Loc.T("Drag an Area from the Hierarchy here to keep it for later. Click in the Scene to add a generic Area."),
        PwContext.Models => Loc.T("No model files in this folder."),
        _ => string.Empty,
    };

    /// <summary>Element type the Project Window currently lists assets for.</summary>
    public ElementType ElementType { get; private set; } = ElementType.Object;

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
            if (_editor.PlacementType == ElementType)
                _editor.PlacementType = null;
            _editor.StatusText = Loc.F("{0} items selected", _selectedItems.Count);
            return;
        }

        if (newValue == null)
            return;
        if (Context == PwContext.Models && !newValue.CanPlace)
        {
            _editor.PlacementType = null;
            _editor.StatusText = Loc.F("{0} cannot be rendered, so it cannot be placed.", newValue.Label);
            return;
        }
        // Picking an asset arms placement: the next click in the Scene creates it.
        _editor.PlacementType = ElementType;
    }
}
