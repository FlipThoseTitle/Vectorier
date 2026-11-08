using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Services;
using Vectorier.Services.Assets;
using Vectorier.Services.IO;
using Vectorier.Services.Localization;
using Vectorier.Services.Projects;
using Vectorier.ViewModels.ProjectManager;

namespace Vectorier.ViewModels.Editor;

public partial class ProjectWindowViewModel
{
    /// <summary>"Import Textures..." / "Import Models...": picks files and folders together, then imports them.</summary>
    [RelayCommand]
    public async Task Import()
    {
        if (!CanImport)
            return;
        bool models = Context == PwContext.Models;
        var dialog = models
            ? new ViewModels.Dialogs.ImportDialogViewModel(Loc.T("Import Models"),
                Loc.T("Model XML files, with their folder (a folder of the same name). Folders holding model XML files import everything in them."),
                Loc.T("Model XML"), new[] { "*.xml" })
            : new ViewModels.Dialogs.ImportDialogViewModel(Loc.T("Import Textures"),
                Loc.T("Images (with their .plist / .json for animated textures). Folders are imported recursively and become folders here."),
                Loc.T("Textures"), new[] { "*.png", "*.jpg", "*.jpeg", "*.plist", "*.json" });
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
            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Import Models"), Loc.T("No model XML file or folder was selected."), MessageKind.Info);
            return;
        }
        try
        {
            IsBusy = true;
            string root = _editor.Project.ModelsFolder;
            await AppServices.FileSystem.CreateDirectoryAsync(root);
            var imported = new List<string>();
            var replace = new ReplaceAsker(Loc.T("Import Models"));
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
                    if (!await replace.AskAsync(Loc.F("The folder {0} already exists in models. Replace it?", folder.Name)))
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
            _editor.StatusText = Loc.F("Imported {0} model(s) and {1} folder(s)", imported.Count, copiedFolders);
            await ReloadAsync();
            await RevealNewAsync(imported);
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Import Models"), ex.Message, MessageKind.Error);
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

            var replaceTexture = new ReplaceAsker(Loc.T("Import"));
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
            _editor.StatusText = Loc.F("Imported {0} texture(s)", imported.Count);
            await ReloadAsync();
            await RevealNewAsync(imported.Select(n => Textures.Find(n)?.FileName).OfType<string>().ToList());
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Import"), ex.Message, MessageKind.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
