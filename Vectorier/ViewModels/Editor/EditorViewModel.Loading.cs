using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vectorier.Models.Scene;
using Vectorier.Services;
using Vectorier.Services.IO;
using Vectorier.Services.Localization;
using Vectorier.Services.Xml;

namespace Vectorier.ViewModels.Editor;

public partial class EditorViewModel
{
    private async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            await Context.EditorCache.EnsureLoadedAsync();
            if (!Context.Textures.IsIndexed)
                await Context.Textures.RefreshIndexAsync();

            if (_initialLevelPath != null && await AppServices.FileSystem.FileExistsAsync(_initialLevelPath))
            {
                await LoadFromPathAsync(_initialLevelPath, _initialLevelName);
            }
            else
            {
                // A level created in the Project Manager that has no XML yet: it stays in memory until saved.
                var doc = new SceneDocument { LevelName = _initialLevelName ?? NewLevelName, FilePath = _initialLevelPath };
                await ResolveDefinitionsAsync(doc.Settings);
                SetDocument(doc, null);
            }
            ProjectWindow.SetContext(ActiveElementType?.Type ?? ElementType.Object);
            _ = EnsureTrickTriggersAsync();
            // The game gets its files and moves ready meanwhile: Play starts right away.
            _ = Services.Game.GameHost.Current.WarmUpAsync(Project);
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

    private async Task LoadFromPathAsync(string path, string? levelName)
    {
        byte[] data = await AppServices.FileSystem.ReadAllBytesAsync(path);
        var doc = await LoadDocumentAsync(data, levelName ?? VPath.GetFileNameWithoutExtension(path));
        doc.FilePath = path;
        SetDocument(doc, null);
    }

    private async Task<SceneDocument> LoadDocumentAsync(byte[] data, string levelName)
    {
        StatusText = Loc.F("Opening {0}...", levelName);
        var xml = await Task.Run(() => XmlHelpers.ParseDocument(data));
        var (city, ground) = SceneXmlReader.ReadSets(xml);
        Definitions = await ResolveAllSetsAsync(ground, city);
        CollabReader = new SceneXmlReader(Definitions);
        var reader = CollabReader;
        var doc = await Task.Run(() => reader.ReadLevel(xml, levelName));

        RestoreTriggerTypes(doc);

        var warnings = reader.Warnings.ToList();
        if (Definitions.MissingFiles.Count > 0)
            warnings.Insert(0, Loc.T("Missing set files: ") + string.Join(", ", Definitions.MissingFiles));
        StatusText = warnings.Count > 0 ? warnings[0] : Loc.F("Opened {0}.xml", levelName);
        return doc;
    }

    // ================= TRIGGER VARIABLE TYPES (editor cache) ================= //

    private string TypeCacheLevel => Document.FilePath ?? Document.LevelName;

    /// <summary>Gives each trigger of a freshly read level the variable types cached for it.</summary>
    private void RestoreTriggerTypes(SceneDocument doc)
    {
        string level = doc.FilePath ?? doc.LevelName;
        foreach (var trigger in doc.AllElements().OfType<TriggerElement>())
        {
            if (Context.EditorCache.FindTriggerTypes(level, trigger.TypeCacheKey) is { } types)
                trigger.VariableTypes = new Dictionary<string, string>(types);
        }
    }

    /// <summary>Types cached for a trigger that has none yet (pasted, stored in the Project Window...).</summary>
    public Dictionary<string, string>? FindCachedTriggerTypes(TriggerElement trigger) =>
        Context.EditorCache.FindTriggerTypes(TypeCacheLevel, trigger.TypeCacheKey);

    /// <summary>Called by the Trigger Editor's Apply: caches the trigger's types right away.</summary>
    public async Task CacheTriggerTypesAsync(TriggerElement trigger)
    {
        Context.EditorCache.SetTriggerTypes(TypeCacheLevel, trigger.TypeCacheKey, trigger.VariableTypes);
        await Context.EditorCache.SaveAsync();
    }

    /// <summary>On save: the level's cache entries become exactly its current triggers.</summary>
    private async Task SaveTriggerTypesAsync()
    {
        Context.EditorCache.SetLevelTriggerTypes(TypeCacheLevel,
            Document.AllElements().OfType<TriggerElement>().Select(t => (t.TypeCacheKey, t.VariableTypes)));
        await Context.EditorCache.SaveAsync();
    }

    private async Task ResolveDefinitionsAsync(LevelSettings settings)
    {
        Definitions = await ResolveAllSetsAsync(settings.GroundSets, settings.CitySets);
        CollabReader = new SceneXmlReader(Definitions);
    }

    // ================= AUTOMATIC SETS ================= //

    /// <summary>Object XML file → is a Building file (City) or an Object file (Ground).</summary>
    private readonly Dictionary<string, bool> _fileIsBuilding = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Sets are automatic: every Object XML of the project can be used, so all of them are resolved (Object files
    /// as Ground, Building files as City). The level's own sets come last and win when names collide, so existing
    /// levels resolve exactly as before.
    /// </summary>
    private async Task<DefinitionTable> ResolveAllSetsAsync(IReadOnlyList<string> levelGround, IReadOnlyList<string> levelCity)
    {
        var files = (await ObjectFileOps.ObjectFilesAsync(Project)).Select(VPath.GetFileName).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var file in files)
        {
            try { _fileIsBuilding[file] = await Context.Objects.IsBuildingFileAsync(file); }
            catch { _fileIsBuilding[file] = file.StartsWith("buildings", StringComparison.OrdinalIgnoreCase); }
        }
        var ground = files.Where(f => !IsBuildingFile(f) && !levelGround.Contains(f, StringComparer.OrdinalIgnoreCase)).Concat(levelGround).ToList();
        var city = files.Where(f => IsBuildingFile(f) && !levelCity.Contains(f, StringComparer.OrdinalIgnoreCase)).Concat(levelCity).ToList();
        return await Context.Objects.ResolveSetsAsync(ground, city);
    }

    public bool IsBuildingFile(string file) =>
        _fileIsBuilding.TryGetValue(file, out bool building) ? building : file.StartsWith("buildings", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The level's Sets from the objects it uses: the file of every definition placed in the level, Building files
    /// as City and Object files as Ground (files already listed keep their order).
    /// </summary>
    public (List<string> City, List<string> Ground) ComputeAutoSets(SceneDocument? document = null)
    {
        document ??= ObjectEdit?.LevelDocument ?? Document;
        var used = document.AllElements().OfType<ObjectElement>()
            .Select(o => o.Reference?.SourceFile)
            .Where(f => !string.IsNullOrEmpty(f))
            .Select(f => f!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        List<string> Ordered(IEnumerable<string> previous, bool building)
        {
            var wanted = used.Where(f => IsBuildingFile(f) == building).ToList();
            var result = previous.Where(p => wanted.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();
            result.AddRange(wanted.Where(w => !result.Contains(w, StringComparer.OrdinalIgnoreCase)).OrderBy(w => w, StringComparer.OrdinalIgnoreCase));
            return result;
        }
        return (Ordered(document.Settings.CitySets, true), Ordered(document.Settings.GroundSets, false));
    }

    private void ApplyAutoSets()
    {
        var (city, ground) = ComputeAutoSets(Document);
        Document.Settings.CitySets = city;
        Document.Settings.GroundSets = ground;
    }

    /// <summary>Re-links every named Object with its definition (after the sets or names changed).</summary>
    public void ResolveReferences()
    {
        foreach (var element in Document.AllElements())
        {
            if (element is ObjectElement obj)
                obj.Reference = SafeReference(obj.Name);
        }
    }

    public ObjectDefinition? ResolveDefinitionName(string? name) => string.IsNullOrEmpty(name) ? null : CollabReader.ResolveDefinition(name);

    /// <summary>
    /// Object XML files changed (definitions saved, deleted, files removed): every file is resolved again. Objects
    /// without a definition whose name now has one become instances of it (their inline children are replaced, as
    /// Objects with a definition cannot have children); instances whose definition is gone show as MISSING.
    /// </summary>
    public async Task RefreshDefinitionsAsync(IEnumerable<string>? changedFiles = null)
    {
        if (changedFiles != null)
            foreach (var file in changedFiles)
                Context.Objects.Invalidate(file);
        else
            Context.Objects.Invalidate();
        var level = ObjectEdit?.LevelDocument ?? Document;
        await ResolveDefinitionsAsync(level.Settings);
        bool changed = false;
        foreach (var obj in Document.AllElements().OfType<ObjectElement>().ToList())
        {
            var definition = SafeReference(obj.Name);
            if (definition != null && obj.Reference == null && obj.Children.Count > 0)
            {
                foreach (var child in obj.Children.ToList())
                {
                    if (child.IsSelected)
                    {
                        child.IsSelected = false;
                        _selection.Remove(child);
                    }
                    obj.RemoveChild(child);
                }
                changed = true;
            }
            using (SuppressTracking())
                obj.Reference = definition;
        }
        if (changed)
            MarkChanged();
        RaiseStructureChanged();
        RaiseSelectionChanged();
    }

    /// <summary>Builds (once) a definition's content so it can be previewed or placed.</summary>
    public void BuildDefinition(ObjectDefinition definition) => CollabReader.BuildDefinition(definition);

    private void SetDocument(SceneDocument doc, IEnumerable<int>? selectIds)
    {
        Paint?.Reset();
        Document = doc;
        _surfacePending.Clear();
        RestoreSurfaces(doc);
        MoveVisualizer?.OnDocumentReplaced();
        ResetHistory();
        ClearSelectionSilently();
        OnPropertyChanged(nameof(Document));
        OnPropertyChanged(nameof(Title));
        RaiseStructureChanged();
        if (selectIds != null)
            SelectByIds(selectIds);
        RaiseSelectionChanged();
    }
}
