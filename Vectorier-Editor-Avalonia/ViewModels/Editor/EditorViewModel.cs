using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier_Editor_Avalonia.Models.Scene;
using Vectorier_Editor_Avalonia.Services;
using Vectorier_Editor_Avalonia.Services.IO;
using Vectorier_Editor_Avalonia.Services.Projects;
using Vectorier_Editor_Avalonia.Services.Xml;
using Vectorier_Editor_Avalonia.ViewModels.ProjectManager;

namespace Vectorier_Editor_Avalonia.ViewModels.Editor;

public enum CenterTab
{
    Scene,
    Game,
    Object,
}

public enum EditorTool
{
    Pan,
    Move,
    Rotate,
    Scale,
    Rect
}

/// <summary>
/// The Level Editor screen. Owns the open <see cref="SceneDocument"/>, the selection, undo history and all
/// editing commands; the Hierarchy, Scene, Inspector and Project Window all work through it.
/// </summary>
public sealed partial class EditorViewModel : ViewModelBase, IScreen
{
    public const string NewLevelName = "New_Level";

    private readonly string? _initialLevelName;
    private readonly string? _initialLevelPath;
    private SceneXmlReader _reader = new(null);

    public EditorViewModel(ProjectInfo project, string? levelName, string? levelPath)
    {
        Project = project;
        Context = ProjectContext.For(project);
        _initialLevelName = levelName;
        _initialLevelPath = levelPath;

        foreach (var type in ElementTypeInfo.MenuOrder)
        {
            var info = new ElementTypeInfo(type, ElementTypes.Count);
            info.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ElementTypeInfo.IsVisible))
                    SceneInvalidated?.Invoke();
            };
            ElementTypes.Add(info);
        }

        Hierarchy = new HierarchyViewModel(this);
        Inspector = new InspectorViewModel(this);
        ProjectWindow = new ProjectWindowViewModel(this);
        Dynamics = new DynamicEditorViewModel(this);
        MoveVisualizer = new MoveVisualizerViewModel(this);

        SetDocument(new SceneDocument { LevelName = levelName ?? NewLevelName }, null);
        ActiveElementType = ElementTypes[0];
        _ = InitializeAsync();
    }

    public ProjectInfo Project { get; }
    public ProjectContext Context { get; }

    public HierarchyViewModel Hierarchy { get; }
    public InspectorViewModel Inspector { get; }
    public ProjectWindowViewModel ProjectWindow { get; }
    public DynamicEditorViewModel Dynamics { get; }
    public MoveVisualizerViewModel MoveVisualizer { get; }

    public ObservableCollection<ElementTypeInfo> ElementTypes { get; } = new();

    public SceneDocument Document { get; private set; } = new();

    /// <summary>Object definitions visible to the level through its &lt;Sets&gt;.</summary>
    public DefinitionTable Definitions { get; private set; } = new();

    public bool CanPlay => AppServices.Platform.CanLaunchGame;

    // ================= SCREEN ================= //

    public string Title => ObjectEdit is { } edit
        ? $"Vectorier - {Project.Name} - Editing object {edit.Name} ({edit.File}){(IsDirty ? " *" : string.Empty)}"
        : $"Vectorier - {Project.Name} - {Document.LevelName}.xml{(IsDirty ? " *" : string.Empty)}";
    public Size PreferredSize => new(1600, 900);
    public bool StartMaximized => true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private bool _isDirty;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private EditorTool _activeTool = EditorTool.Move;
    [ObservableProperty] private ElementTypeInfo? _activeElementType;

    /// <summary>When set, a left click in the Scene creates an element of this type instead of selecting.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlacementHint), nameof(IsPlacing), nameof(ShowPlacementHint))]
    private ElementType? _placementType;

    /// <summary>The Trigger Editor shown over the Scene, or null.</summary>
    [ObservableProperty] private TriggerEditorViewModel? _triggerEditor;

    /// <summary>Bottom panel: the Dynamic Editor tab is shown instead of the Project Window.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProjectTab))]
    private bool _isDynamicEditorTab;

    /// <summary>Right panel: the Move Visualizer tab is shown instead of the Inspector.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInspectorTab))]
    private bool _isMoveVisualizerTab;

    /// <summary>Center panel tab: Scene, read-only Game View, or the Object being edited.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSceneViewTab), nameof(IsGameViewTab), nameof(IsObjectTab), nameof(ShowsSceneView), nameof(ShowPlacementHint))]
    private CenterTab _centerTab;

    public bool IsSceneViewTab
    {
        get => CenterTab == CenterTab.Scene;
        set { if (value) CenterTab = CenterTab.Scene; }
    }

    public bool IsGameViewTab
    {
        get => CenterTab == CenterTab.Game;
        set { if (value) CenterTab = CenterTab.Game; else if (CenterTab == CenterTab.Game) CenterTab = CenterTab.Scene; }
    }

    public bool IsObjectTab
    {
        get => CenterTab == CenterTab.Object;
        set { if (value && ObjectEdit != null) CenterTab = CenterTab.Object; }
    }

    /// <summary>The editable Scene control is shown (Scene tab, or the Object tab).</summary>
    public bool ShowsSceneView => CenterTab != CenterTab.Game;

    public bool ShowPlacementHint => IsPlacing && ShowsSceneView;

    public bool IsProjectTab
    {
        get => !IsDynamicEditorTab;
        set => IsDynamicEditorTab = !value;
    }

    public bool IsInspectorTab
    {
        get => !IsMoveVisualizerTab;
        set => IsMoveVisualizerTab = !value;
    }

    public bool IsPlacing => PlacementType != null;

    public string PlacementHint => PlacementType is { } t ? $"Click in the Scene to place {t}" + (t is ElementType.Platform or ElementType.Trigger or ElementType.Area or ElementType.Comment ? " (drag to size)" : string.Empty) + " · Esc to stop" : string.Empty;

    /// <summary>Raised whenever the Scene must be redrawn.</summary>
    public event Action? SceneInvalidated;

    /// <summary>Raised when elements were added, removed or re-parented (or a whole new state was loaded).</summary>
    public event Action? StructureChanged;

    /// <summary>Raised when a property of an element changes.</summary>
    public event Action<SceneElement, string?>? ElementChanged;

    /// <summary>Asks the Scene to center on an element.</summary>
    public event Action<SceneElement>? FrameRequested;

    public void InvalidateScene() => SceneInvalidated?.Invoke();

    public void RequestFrame(SceneElement element) => FrameRequested?.Invoke(element);

    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(Title));

    partial void OnActiveElementTypeChanged(ElementTypeInfo? oldValue, ElementTypeInfo? newValue)
    {
        if (oldValue != null) oldValue.IsActive = false;
        if (newValue != null) newValue.IsActive = true;
        ProjectWindow.SetContext(newValue?.Type ?? ElementType.Object);
    }

    partial void OnActiveToolChanged(EditorTool value) => ExitPlacement();

    /// <summary>Element types that are placed from an asset picked in the Project Window.</summary>
    public static bool RequiresAsset(ElementType type) =>
        type is ElementType.Object or ElementType.Image or ElementType.Model or ElementType.Animation or ElementType.Particle;

    /// <summary>
    /// Toolbar tool picked (button or hotkey, even the tool already active): placement ends. Types placed from a
    /// Project Window asset keep their Element Bar highlight but the asset is deselected; the others lose it.
    /// </summary>
    public void SelectTool(EditorTool tool)
    {
        if (tool == EditorTool.Pan)
            tool = EditorTool.Move; // panning is the right mouse button
        if (ActiveTool != tool)
            ActiveTool = tool; // calls ExitPlacement
        else
            ExitPlacement();
    }

    public void ExitPlacement()
    {
        PlacementType = null;
        if (ActiveElementType is not { } info)
            return;
        if (RequiresAsset(info.Type))
            ProjectWindow.SelectedItem = null;
        else
            info.IsActive = false;
    }

    /// <summary>Gizmo axes follow the element's rotation (Local) or the screen (Global, default).</summary>
    [ObservableProperty] private bool _isLocalMode;

    /// <summary>Gizmos sit at the element's pivot (its top-left position) or at the selection's center (default).</summary>
    [ObservableProperty] private bool _usePivot;

    partial void OnIsLocalModeChanged(bool value) => SceneInvalidated?.Invoke();
    partial void OnUsePivotChanged(bool value) => SceneInvalidated?.Invoke();

    [RelayCommand]
    private void ToggleLocalMode() => IsLocalMode = !IsLocalMode;

    [RelayCommand]
    private void TogglePivot() => UsePivot = !UsePivot;

    /// <summary>{ / } : Order in Layer of the selected elements −1 / +1.</summary>
    public void ChangeOrder(int delta)
    {
        if (!HasSelection)
            return;
        BeginGesture();
        foreach (var e in Selection)
            e.Order += delta;
        EndGesture();
        // Drawing order depends on it: the Scene sorts again.
        RaiseStructureChanged();
        StatusText = Selection.Count == 1 ? $"Order in Layer: {Selection[0].Order}" : $"Order in Layer {(delta > 0 ? "+" : "")}{delta} ({Selection.Count} elements)";
    }

    partial void OnPlacementTypeChanged(ElementType? value) => SceneInvalidated?.Invoke();

    /// <summary>Element Bar click / number key: switch the Project Window and arm placement where it applies.</summary>
    public void ChooseElementType(ElementTypeInfo info)
    {
        ActiveElementType = info;
        info.IsActive = true;
        // Object, Image, Model, Animation and Particle need an asset picked in the Project Window first.
        PlacementType = RequiresAsset(info.Type)
            ? (ProjectWindow.HasPlaceableSelection ? info.Type : null)
            : info.Type;
    }

    [RelayCommand]
    private void SetTool(EditorTool tool) => SelectTool(tool);

    [RelayCommand]
    private void StopPlacing() => ExitPlacement();

    public bool IsTypeVisible(ElementType type)
    {
        foreach (var info in ElementTypes)
        {
            if (info.Type == type)
                return info.IsVisible;
        }
        return true;
    }

    // ================= LOADING ================= //

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
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync("Open Level", "The level could not be opened:\n" + ex.Message, MessageKind.Error);
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
        StatusText = $"Opening {levelName}...";
        var xml = await Task.Run(() => XmlHelpers.ParseDocument(data));
        var (city, ground) = SceneXmlReader.ReadSets(xml);
        Definitions = await ResolveAllSetsAsync(ground, city);
        _reader = new SceneXmlReader(Definitions);
        var reader = _reader;
        var doc = await Task.Run(() => reader.ReadLevel(xml, levelName));

        RestoreTriggerTypes(doc);

        var warnings = reader.Warnings.ToList();
        if (Definitions.MissingFiles.Count > 0)
            warnings.Insert(0, "Missing set files: " + string.Join(", ", Definitions.MissingFiles));
        StatusText = warnings.Count > 0 ? warnings[0] : $"Opened {levelName}.xml";
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
        _reader = new SceneXmlReader(Definitions);
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
                obj.Reference = string.IsNullOrEmpty(obj.Name) ? null : _reader.ResolveDefinition(obj.Name);
        }
    }

    public ObjectDefinition? ResolveDefinitionName(string? name) => string.IsNullOrEmpty(name) ? null : _reader.ResolveDefinition(name);

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
            var definition = ResolveDefinitionName(obj.Name);
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
    public void BuildDefinition(ObjectDefinition definition) => _reader.BuildDefinition(definition);

    public SceneXmlReader CreateReader(DefinitionTable? table) => new(table);

    private void SetDocument(SceneDocument doc, IEnumerable<int>? selectIds)
    {
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

    // ================= FILE COMMANDS ================= //

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
        if (!await ConfirmDiscardAsync())
            return;
        var doc = new SceneDocument { LevelName = NewLevelName };
        await ResolveDefinitionsAsync(doc.Settings);
        SetDocument(doc, null);
        StatusText = "New level";
    }

    [RelayCommand]
    private async Task Open()
    {
        var topLevel = AppServices.Shell.CurrentTopLevel;
        if (topLevel == null)
            return;
        if (!await ConfirmDiscardAsync())
            return;

        var start = await AppServices.FileSystem.GetStorageFolderAsync(Project.LevelsFolder, topLevel.StorageProvider);
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Level",
            AllowMultiple = false,
            SuggestedStartLocation = start,
            FileTypeFilter = new[] { new FilePickerFileType("Vector Level XML") { Patterns = new[] { "*.xml" } } },
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
            await AppServices.Dialogs.ShowMessageAsync("Open Level", "The level could not be opened:\n" + ex.Message, MessageKind.Error);
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
        if (!Document.HasLocation)
            return await SaveAsAsync();
        return await WriteDocumentAsync();
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
            Title = "Save Level As",
            SuggestedFileName = suggested,
            SuggestedStartLocation = start,
            DefaultExtension = "xml",
            ShowOverwritePrompt = true,
            FileTypeChoices = new[] { new FilePickerFileType("Vector Level XML") { Patterns = new[] { "*.xml" } } },
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
                    StatusText = $"Saved as {normalized}.xml (level names use upper case and underscores)";
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
        TextureSizeProvider = name => Context.Textures.TryGet(name) is { } asset ? (asset.Width, asset.Height) : null,
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
            StatusText = $"Saved {Document.LevelName}.xml";
            if (errors.Count > 0)
                await AppServices.Dialogs.ShowMessageAsync("Save", string.Join("\n", errors), MessageKind.Warning);
            return true;
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync("Save", "The level could not be saved:\n" + ex.Message, MessageKind.Error);
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
            string levelName = Document.LevelName;
            string target = VPath.Combine(Project.LevelsFolder, levelName + ".xml");
            byte[] data = ExportXml(out _);
            await AppServices.FileSystem.CreateDirectoryAsync(Project.LevelsFolder);
            await AppServices.FileSystem.WriteAllBytesAsync(target, data);
            if (string.Equals(Document.FilePath, target, StringComparison.Ordinal))
                MarkSaved();

            string? error = AppServices.Platform.LaunchGame(AppServices.FileSystem, Project.Name, levelName, hunter);
            if (error != null)
                await AppServices.Dialogs.ShowMessageAsync("Play Level", error, MessageKind.Error);
            else
                StatusText = hunter ? $"Playing {levelName} in Hunter Mode" : $"Playing {levelName}";
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync("Play Level", ex.Message, MessageKind.Error);
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
        if (!await ConfirmDiscardAsync())
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
        return await ConfirmDiscardAsync();
    }

    public void OnClosed()
    {
        MoveVisualizer.Stop();
        Dynamics.StopPreview();
    }

    // ================= TRIGGER EDITOR ================= //

    public void OpenTriggerEditor(TriggerElement trigger)
    {
        TriggerEditor?.Close();
        TriggerEditor = new TriggerEditorViewModel(this, trigger);
    }

    public void CloseTriggerEditor() => TriggerEditor = null;
}
