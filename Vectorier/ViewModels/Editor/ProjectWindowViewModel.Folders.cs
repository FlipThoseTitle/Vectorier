using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Models.Scene;
using Vectorier.Services;
using Vectorier.Services.Assets;
using Vectorier.Services.IO;
using Vectorier.Services.Localization;
using Vectorier.Services.Projects;
using Vectorier.Services.Xml;

namespace Vectorier.ViewModels.Editor;

public partial class ProjectWindowViewModel
{
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
            item.Status = Loc.T("Could not be read");
            return;
        }
        if (!model.IsSelfContained)
        {
            item.Status = model.Problem;
            return;
        }
        try
        {
            item.Silhouette = model.BuildSilhouette(out _);
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
            item.Status = Loc.T("Not rendered: ") + ex.Message;
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
        string? name = await AppServices.Dialogs.PromptAsync(Loc.T("New Folder"), Loc.T("Folder name:"), Cache.UniqueFolderName(key, "New Folder"),
            text => string.IsNullOrWhiteSpace(text) ? Loc.T("Enter a name.") :
                Cache.GetFolders(key).Any(f => string.Equals(f.Name, text.Trim(), StringComparison.OrdinalIgnoreCase)) ? Loc.T("A folder with this name already exists.") :
                text.Trim() is "Unorganized" or "All" ? Loc.T("This name is reserved.") : null);
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
        string? input = await AppServices.Dialogs.PromptAsync(Loc.T("New Object XML File"), Loc.T("Name (e.g. \"my cool object\" is saved as my_cool_object.xml in xmlroot):"), "new objects",
            text =>
            {
                if (string.IsNullOrWhiteSpace(text)) return Loc.T("Enter a name.");
                string file = MakeObjectFileName(text);
                if (file == ".xml") return Loc.T("Use letters or numbers.");
                if (string.Equals(file, "TriggerTemplates.xml", StringComparison.OrdinalIgnoreCase)) return Loc.T("This name is reserved.");
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
        string? name = await AppServices.Dialogs.PromptAsync(Loc.T("Rename Folder"), Loc.T("Folder name:"), folder.Name,
            text => string.IsNullOrWhiteSpace(text) ? Loc.T("Enter a name.") :
                !string.Equals(text.Trim(), folder.Name, StringComparison.OrdinalIgnoreCase) &&
                Cache.GetFolders(key).Any(f => string.Equals(f.Name, text.Trim(), StringComparison.OrdinalIgnoreCase)) ? Loc.T("A folder with this name already exists.") : null);
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
        ItemsDeleted?.Invoke(); // the placement preview goes first (it may be one of their objects)
        try
        {
            if (files.Count > 0)
            {
                if (_editor.ObjectEdit is { } edit && files.Contains(edit.File, StringComparer.OrdinalIgnoreCase))
                {
                    await AppServices.Dialogs.ShowMessageAsync(Loc.T("Delete Object XML"), Loc.F("{0} is open in the Object tab. Return to the Scene tab first.", edit.File), MessageKind.Info);
                    return;
                }
                string list = files.Count == 1 ? files[0] : $"{files.Count} files ({string.Join(", ", files.Take(4))}{(files.Count > 4 ? ", ..." : "")})";
                if (!await AppServices.Dialogs.ConfirmAsync(Loc.T("Delete Object XML"),
                        Loc.F("Delete {0} from xmlroot?\n\nThis cannot be undone. Objects of {1} used in levels will show as MISSING.", list, (files.Count == 1 ? "this file" : "these files")),
                        Loc.T("Delete"), Loc.T("Cancel"), MessageKind.Warning))
                    return;
                foreach (var file in files)
                {
                    await AppServices.FileSystem.DeleteFileAsync(VPath.Combine(_editor.Project.XmlRoot, file));
                    Cache.Data.ObjectFileOrder.Remove(file);
                }
                await Cache.SaveAsync();
                await _editor.RefreshDefinitionsAsync(files);
                _editor.StatusText = Loc.F("Deleted {0}", string.Join(", ", files));
                await ReloadAsync();
                ItemsDeleted?.Invoke();
                return;
            }
            if (userFolders.Count == 0)
                return;
            string names = userFolders.Count == 1 ? $"the folder '{userFolders[0].Name}'" : $"{userFolders.Count} folders";
            if (!await AppServices.Dialogs.ConfirmAsync(Loc.T("Delete Folder"), Loc.F("Delete {0}? Their items move to Unorganized; no files are deleted.", names), Loc.T("Delete")))
                return;
            var removed = userFolders.Select(f => f.Name).ToHashSet();
            Cache.GetFolders(CacheKey).RemoveAll(f => removed.Contains(f.Name));
            await Cache.SaveAsync();
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Delete"), ex.Message, MessageKind.Error);
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
}
