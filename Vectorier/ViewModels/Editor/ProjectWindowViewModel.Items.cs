using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Services;
using Vectorier.Services.Assets;
using Vectorier.Services.IO;
using Vectorier.Services.Localization;

namespace Vectorier.ViewModels.Editor;

public partial class ProjectWindowViewModel
{
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
                        string? name = await AppServices.Dialogs.PromptAsync(Loc.T("Rename Texture"), Loc.T("Texture name:"), entry.Name,
                            text => string.IsNullOrWhiteSpace(text) ? Loc.T("Enter a name.") :
                                text.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || text.Contains('/') ? Loc.T("The name contains invalid characters.") : null);
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
                        _editor.StatusText = Loc.F("Renamed {0} to {1}. Levels using the old name are not changed.", entry.Name, name);
                        break;
                    }
                case PwContext.Triggers when item.Trigger != null:
                    {
                        string? name = await AppServices.Dialogs.PromptAsync(Loc.T("Rename Trigger"), Loc.T("Name:"), item.Trigger.Name, t => string.IsNullOrWhiteSpace(t) ? Loc.T("Enter a name.") : null);
                        if (name == null) return;
                        item.Trigger.Name = name;
                        await Cache.SaveAsync();
                        break;
                    }
                case PwContext.Areas when item.Area != null:
                    {
                        string? name = await AppServices.Dialogs.PromptAsync(Loc.T("Rename Area"), Loc.T("Name:"), item.Area.Name, t => string.IsNullOrWhiteSpace(t) ? Loc.T("Enter a name.") : null);
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
            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Rename"), ex.Message, MessageKind.Error);
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
        // The Scene's placement preview may draw one of them (its bitmap, its model): it goes before anything is
        // deleted (it comes back on the next pointer move if nothing was).
        ItemsDeleted?.Invoke();
        try
        {
            switch (Context)
            {
                case PwContext.Textures:
                    {
                        var deletable = items.Where(i => i.Texture != null && !TextureLibrary.IsProtected(i.Texture)).ToList();
                        if (deletable.Count == 0)
                        {
                            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Delete Texture"), Loc.T("The selected textures are required by the game and cannot be deleted."), MessageKind.Warning);
                            return;
                        }
                        string kept = deletable.Count < items.Count ? $"\n({items.Count - deletable.Count} texture(s) required by the game are kept.)" : "";
                        if (!await AppServices.Dialogs.ConfirmAsync(Loc.T("Delete Texture"),
                                Loc.F("Delete {0} from the project? Levels using them will show them as missing.{1}", ListNames(deletable.Select(d => d.Texture!.FileName).ToList()), kept),
                                Loc.T("Delete"), kind: MessageKind.Warning))
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
                        if (!await AppServices.Dialogs.ConfirmAsync(Context == PwContext.Triggers ? Loc.T("Delete Trigger") : Loc.T("Delete Area"),
                                Loc.F("Remove {0} from the Project Window?", ListNames(items.Select(i => i.Label).ToList())), Loc.T("Delete"), kind: MessageKind.Warning))
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
                            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Delete Object"), Loc.F("'{0}' is open in the Object tab. Return to the Scene tab first.", edit.Name), MessageKind.Info);
                            return;
                        }
                        if (!await AppServices.Dialogs.ConfirmAsync(Loc.T("Delete Object"),
                                Loc.F("Remove {0} from {1}?\n\nLevels that use {2} will show {3} as MISSING.", ListNames(names), file, (names.Count == 1 ? "it" : "them"), (names.Count == 1 ? "it" : "them")),
                                Loc.T("Delete"), kind: MessageKind.Warning))
                            return;
                        await RemoveDefinitionsAsync(file, names);
                        await _editor.RefreshDefinitionsAsync(new[] { file });
                        _editor.StatusText = Loc.F("Removed {0} from {1}", ListNames(names), file);
                        break;
                    }
                case PwContext.Models:
                    {
                        var names = items.Where(i => i.ModelName != null).Select(i => i.ModelName!).ToList();
                        if (!await AppServices.Dialogs.ConfirmAsync(Loc.T("Delete Model"),
                                Loc.F("Delete {0} (and the model folder of the same name, if any) from the project? Levels using them will show them as missing.", ListNames(names.Select(n => n + ".xml").ToList())),
                                Loc.T("Delete"), kind: MessageKind.Warning))
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
                        _editor.StatusText = Loc.F("Deleted {0}", ListNames(names));
                        break;
                    }
                default:
                    return;
            }
            await Cache.SaveAsync();
            await ReloadAsync();
            ItemsDeleted?.Invoke();
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Delete"), ex.Message, MessageKind.Error);
        }
    }
}
