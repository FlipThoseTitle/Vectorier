using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Models.Scene;
using Vectorier.Services;
using Vectorier.Services.Collab;
using Vectorier.Services.IO;
using Vectorier.Services.Localization;
using Vectorier.Services.Xml;
using Vectorier.ViewModels.ProjectManager;

namespace Vectorier.ViewModels.Editor;

public partial class EditorViewModel
{
    /// <summary>Asks to save unsaved changes. Returns false when the user cancelled.</summary>
    private async Task<bool> ConfirmDiscardAsync()
    {
        CommitPending();
        if (!IsDirty)
            return true;
        var choice = await AppServices.Dialogs.AskSaveChangesAsync(Document.LevelName + ".xml");
        return choice switch
        {
            SaveChoice.Save => await SaveAsync(),
            SaveChoice.DontSave => true,
            _ => false,
        };
    }

    [RelayCommand]
    private async Task NewLevel()
    {
        if (!await ConfirmLeaveCollabAsync() || !await ConfirmDiscardAsync())
            return;
        var doc = new SceneDocument { LevelName = NewLevelName };
        await ResolveDefinitionsAsync(doc.Settings);
        SetDocument(doc, null);
        StatusText = Loc.T("New level");
    }

    [RelayCommand]
    private async Task Open()
    {
        var topLevel = AppServices.Shell.CurrentTopLevel;
        if (topLevel == null)
            return;
        if (!await ConfirmLeaveCollabAsync() || !await ConfirmDiscardAsync())
            return;

        var start = await AppServices.FileSystem.GetStorageFolderAsync(Project.LevelsFolder, topLevel.StorageProvider);
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("Open Level"),
            AllowMultiple = false,
            SuggestedStartLocation = start,
            FileTypeFilter = new[] { new FilePickerFileType(Loc.T("Vector Level XML")) { Patterns = new[] { "*.xml" } } },
        });
        var file = files.FirstOrDefault();
        if (file == null)
            return;

        try
        {
            IsLoading = true;
            string name = VPath.GetFileNameWithoutExtension(file.Name);
            string? relative = file.TryGetLocalPath() is { } local ? AppServices.FileSystem.TryGetRelativePath(local) : null;
            if (relative != null)
            {
                await LoadFromPathAsync(relative, name);
            }
            else
            {
                byte[] data;
                await using (var stream = await file.OpenReadAsync())
                using (var memory = new MemoryStream())
                {
                    await stream.CopyToAsync(memory);
                    data = memory.ToArray();
                }
                var doc = await LoadDocumentAsync(data, name);
                doc.ExternalFile = file;
                SetDocument(doc, null);
            }
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Open Level"), Loc.T("The level could not be opened:\n") + ex.Message, MessageKind.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private Task<bool> Save() => SaveAsync();

    public async Task<bool> SaveAsync()
    {
        CommitPending();
        if (ObjectEdit != null)
            return await SaveObjectAsync();
        bool saved = !Document.HasLocation ? await SaveAsAsync() : await WriteDocumentAsync();
        if (saved)
            Services.Tutorial.TutorialSignals.Saves++;
        return saved;
    }

    [RelayCommand]
    private Task<bool> SaveAs() => SaveAsAsync();

    private static readonly Regex InvalidLevelChars = new("[^A-Za-z0-9_]", RegexOptions.Compiled);

    /// <summary>Level file names use Vector's convention: upper case, words joined with underscores.</summary>
    public static string NormalizeLevelName(string name)
    {
        name = InvalidLevelChars.Replace(name.Trim().Replace(' ', '_'), "_");
        while (name.Contains("__", StringComparison.Ordinal))
            name = name.Replace("__", "_");
        name = name.Trim('_').ToUpperInvariant();
        return string.IsNullOrEmpty(name) ? NewLevelName.ToUpperInvariant() : name;
    }

    public async Task<bool> SaveAsAsync()
    {
        var topLevel = AppServices.Shell.CurrentTopLevel;
        if (topLevel == null)
            return false;
        CommitPending();

        string suggested = (Document.LevelName == NewLevelName ? NewLevelName : NormalizeLevelName(Document.LevelName)) + ".xml";
        var start = await AppServices.FileSystem.GetStorageFolderAsync(Project.LevelsFolder, topLevel.StorageProvider);
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Loc.T("Save Level As"),
            SuggestedFileName = suggested,
            SuggestedStartLocation = start,
            DefaultExtension = "xml",
            ShowOverwritePrompt = true,
            FileTypeChoices = new[] { new FilePickerFileType(Loc.T("Vector Level XML")) { Patterns = new[] { "*.xml" } } },
        });
        if (file == null)
            return false;

        string chosen = VPath.GetFileNameWithoutExtension(file.Name);
        string normalized = NormalizeLevelName(chosen);
        string? relative = file.TryGetLocalPath() is { } local ? AppServices.FileSystem.TryGetRelativePath(local) : null;

        if (relative != null)
        {
            // Inside the Vectorier folder: enforce the naming convention on the file name.
            string target = VPath.Combine(VPath.GetDirectory(relative), normalized + ".xml");
            if (!string.Equals(target, relative, StringComparison.Ordinal))
            {
                try { await file.DeleteAsync(); } catch { /* the picker may have created an empty file */ }
                if (normalized != chosen)
                    StatusText = Loc.F("Saved as {0}.xml (level names use upper case and underscores)", normalized);
            }
            Document.FilePath = target;
            Document.ExternalFile = null;
        }
        else
        {
            Document.FilePath = null;
            Document.ExternalFile = file;
        }

        Document.LevelName = relative != null ? normalized : chosen;
        OnPropertyChanged(nameof(Title));
        Hierarchy.Rebuild();
        return await WriteDocumentAsync();
    }

    private SceneXmlWriter CreateWriter() => new()
    {
        TextureSizeProvider = name => Context.Textures.TryGetSize(name),
    };

    /// <summary>Serializes the scene to XML bytes. Errors are reported in <paramref name="errors"/>.</summary>
    public byte[] ExportXml(out List<string> errors)
    {
        // Elements previewed by the Dynamic Editor are written at their rest pose.
        using (Dynamics.SuspendPreview())
        {
            // Sets follow the objects used in the level.
            ApplyAutoSets();
            var writer = CreateWriter();
            var xml = writer.WriteLevel(Document);
            errors = writer.Errors;
            return XmlHelpers.ToBytes(xml);
        }
    }

    private async Task<bool> WriteDocumentAsync()
    {
        try
        {
            byte[] data = ExportXml(out var errors);
            if (Document.FilePath != null)
            {
                await AppServices.FileSystem.CreateDirectoryAsync(VPath.GetDirectory(Document.FilePath));
                await AppServices.FileSystem.WriteAllBytesAsync(Document.FilePath, data);
            }
            else if (Document.ExternalFile != null)
            {
                await using var stream = await Document.ExternalFile.OpenWriteAsync();
                stream.SetLength(0);
                await stream.WriteAsync(data);
            }
            else
            {
                return false;
            }

            MarkSaved();
            await SaveTriggerTypesAsync();
            await SaveSurfacesAsync();
            StatusText = Loc.F("Saved {0}.xml", Document.LevelName);
            if (errors.Count > 0)
                await AppServices.Dialogs.ShowMessageAsync(Loc.T("Save"), string.Join("\n", errors), MessageKind.Warning);
            return true;
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Save"), Loc.T("The level could not be saved:\n") + ex.Message, MessageKind.Error);
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private Task PlayLevel() => PlayAsync(false);

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private Task PlayLevelHunter() => PlayAsync(true);

    private async Task PlayAsync(bool hunter)
    {
        CommitPending();
        try
        {
            // Saved first, as before: the played level is the level on disk.
            string levelName = Document.LevelName;
            string target = VPath.Combine(Project.LevelsFolder, levelName + ".xml");
            byte[] data = ExportXml(out _);
            await AppServices.FileSystem.CreateDirectoryAsync(Project.LevelsFolder);
            await AppServices.FileSystem.WriteAllBytesAsync(target, data);
            if (string.Equals(Document.FilePath, target, StringComparison.Ordinal))
                MarkSaved();

            if (await StartGameAsync(levelName, data, hunter, Loc.T("Play Level")))
                StatusText = hunter ? Loc.F("Playing {0} in Hunter Mode", levelName) : Loc.F("Playing {0}", levelName);
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Play Level"), ex.Message, MessageKind.Error);
        }
    }

    [RelayCommand]
    private async Task OpenLevelSettings()
    {
        CommitPending();
        var dialog = new LevelSettingsViewModel(this, Document.Settings.Clone());
        if (await AppServices.Dialogs.ShowAsync(dialog) is not LevelSettings result)
            return;
        await ApplySettingsAsync(result);
    }

    public async Task ApplySettingsAsync(LevelSettings settings)
    {
        bool setsChanged = !settings.CitySets.SequenceEqual(Document.Settings.CitySets) ||
                           !settings.GroundSets.SequenceEqual(Document.Settings.GroundSets);
        Document.Settings = settings;
        if (setsChanged)
        {
            Context.Objects.Invalidate();
            await ResolveDefinitionsAsync(settings);
            ResolveReferences();
            RaiseStructureChanged();
        }
        MarkChanged();
        CommitPending();
    }

    [RelayCommand]
    private async Task OpenPreferences()
    {
        await AppServices.Dialogs.ShowAsync(new PreferencesViewModel());
        SceneInvalidated?.Invoke();
    }

    [RelayCommand]
    private async Task Quit()
    {
        if (!await ConfirmLeaveCollabAsync() || !await ConfirmDiscardAsync())
            return;
        _closingConfirmed = true;
        string? select = Document.FilePath != null ? Document.LevelName : null;
        AppServices.Shell.Show(new ProjectManagerViewModel(Project, "Levels", select));
    }

    private bool _closingConfirmed;

    public async Task<bool> CanCloseAsync()
    {
        if (_closingConfirmed)
            return true;
        return await ConfirmLeaveCollabAsync() && await ConfirmDiscardAsync();
    }

    /// <summary>The session ended from the host's side: this editor closes without asking anything more.</summary>
    internal void CloseForCollabEnd(bool confirmed = true) => _closingConfirmed = confirmed;

    public void OnClosed()
    {
        CollabSession.Current.EditorClosed(this);
        MoveVisualizer.Stop();
        Dynamics.StopPreview();
        UnhookGame();
    }
}
