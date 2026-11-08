using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Models.Scene;
using Vectorier.Services;
using Vectorier.Services.Assets;
using Vectorier.Services.IO;
using Vectorier.Services.Localization;
using Vectorier.Services.Xml;

namespace Vectorier.ViewModels.Editor;

public partial class ProjectWindowViewModel
{
    public void SetContext(ElementType type)
    {
        ElementType = type;
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
            ElementType.Object => Loc.T("Objects"),
            ElementType.Image => Loc.T("Images"),
            ElementType.Animation => Loc.T("Animations"),
            ElementType.Particle => Loc.T("Particles"),
            ElementType.Trigger => Loc.T("Triggers"),
            ElementType.Area => Loc.T("Areas"),
            ElementType.Model => Loc.T("Models"),
            ElementType.Item => Loc.T("Items"),
            ElementType.Platform => Loc.T("Platforms"),
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
                var candidates = _tutorialFolders == null ? _allFolders : Folders.ToList();
                SelectedFolder = candidates.FirstOrDefault(f => f.Name == previousFolder) ?? Folders.FirstOrDefault() ?? _allFolders.FirstOrDefault();
                if (SelectedFolder == null)
                    await LoadItemsAsync();
            }
        }
        catch (Exception ex)
        {
            _editor.StatusText = Loc.T("Project Window: ") + ex.Message;
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

    /// <summary>The models the Project window lists: files that cannot be rendered cannot be placed either.</summary>
    private async Task<List<string>> ListedModelNamesAsync()
    {
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
        return checks.Where(n => n != null).Select(n => n!).ToList();
    }

    private async Task<List<string>> AllItemKeysAsync() => Context switch
    {
        PwContext.Textures => TextureItems().Select(i => i.Key).ToList(),
        PwContext.Triggers => Cache.Data.Triggers.Select(t => t.Id).ToList(),
        PwContext.Areas => Cache.Data.Areas.Select(a => a.Id).ToList(),
        // Only the models that are listed: a hidden file must not make an Unorganized folder appear.
        PwContext.Models => await ListedModelNamesAsync(),
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
                            _editor.StatusText = Loc.F("Skipped a reference to itself inside: {0} (it would nest forever).", string.Join(", ", cyclic));
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
                        var all = (await ListedModelNamesAsync()).Select(n => new PwItem(n, n) { ModelName = n, CanPlace = false, Status = "Loading..." });
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
            _editor.StatusText = Loc.T("Project Window: ") + ex.Message;
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

    /// <summary>Items were deleted (the Scene's placement preview of one of them must go).</summary>
    public event Action? ItemsDeleted;

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
        var source = _tutorialItems == null ? _allItems : _allItems.Where(_tutorialItems).ToList();
        var filtered = search.Length == 0 ? source.ToList() : source.Where(i => i.Label.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        // Unchanged result (e.g. a reload with the same items): keep the grid as it is.
        if (filtered.Count == Items.Count && filtered.SequenceEqual(Items))
            return;
        Items.Clear();
        foreach (var item in filtered)
            Items.Add(item);
    }
}
