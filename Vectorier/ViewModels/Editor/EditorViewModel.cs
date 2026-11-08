using System;
using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Models.Scene;
using Vectorier.Services;
using Vectorier.Services.Localization;
using Vectorier.Services.Projects;
using Vectorier.Services.Xml;

namespace Vectorier.ViewModels.Editor;

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
    Rect,
    Brush
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
        ProjectWindow.ItemsDeleted += OnProjectItemsDeleted;
        Paint = new PaintViewModel(this);
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

    /// <summary>The Brush tool and its paint session.</summary>
    public PaintViewModel Paint { get; }

    public bool IsBrushTool => ActiveTool == EditorTool.Brush;

    /// <summary>"v1.5.1": the version the app was published with (Publish task), from the app's own assembly.</summary>
    public static string AppVersion { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var assembly = System.Reflection.Assembly.GetEntryAssembly() ?? typeof(EditorViewModel).Assembly;
        string? version = System.Reflection.CustomAttributeExtensions
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(assembly)?.InformationalVersion;
        if (string.IsNullOrEmpty(version))
            version = assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        int plus = version.IndexOf('+');
        return "v" + (plus >= 0 ? version[..plus] : version);
    }
    public MoveVisualizerViewModel MoveVisualizer { get; }

    public ObservableCollection<ElementTypeInfo> ElementTypes { get; } = new();

    public SceneDocument Document { get; private set; } = new();

    /// <summary>Object definitions visible to the level through its &lt;Sets&gt;.</summary>
    public DefinitionTable Definitions { get; private set; } = new();

    /// <summary>The game is part of the editor: a level can always be played (desktop and browser).</summary>
    /// <summary>The level can be played: not while an object is being edited (the Object tab shows its content).</summary>
    public bool CanPlay => ObjectEdit == null;

    // ================= SCREEN ================= //

    public string Title => ObjectEdit is { } edit
        ? Loc.F("Vectorier - {0} - Editing object {1} ({2}){3}", Project.Name, edit.Name, edit.File, (IsDirty ? " *" : string.Empty))
        : $"Vectorier - {Project.Name} - {Document.LevelName}.xml{(IsDirty ? " *" : string.Empty)}";
    public Size PreferredSize => new(1600, 900);
    public bool StartMaximized => true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private bool _isDirty;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = Loc.T("Ready");
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

    public string PlacementHint => PlacementType is { } t ? Loc.F("Click in the Scene to place {0}", t) + (t is ElementType.Platform or ElementType.Trigger or ElementType.Area or ElementType.Comment ? Loc.T(" (drag to size)") : string.Empty) + Loc.T(" · Esc to stop") : string.Empty;

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

    partial void OnActiveToolChanged(EditorTool value)
    {
        ExitPlacement();
        OnPropertyChanged(nameof(IsBrushTool));
        SceneInvalidated?.Invoke();
    }

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
        // Leaving the Brush with a painting open asks first (Discard / Keep Editing).
        if (tool != EditorTool.Brush && Paint.IsActive)
        {
            _ = SwitchFromPaintAsync(tool);
            return;
        }
        if (ActiveTool != tool)
            ActiveTool = tool; // calls ExitPlacement
        else
            ExitPlacement();
    }

    private async System.Threading.Tasks.Task SwitchFromPaintAsync(EditorTool tool)
    {
        if (await Paint.ConfirmDiscardAsync())
            SelectTool(tool);
        else
            OnPropertyChanged(nameof(ActiveTool)); // the toolbar shows the Brush again
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
        StatusText = Selection.Count == 1 ? Loc.F("Order in Layer: {0}", Selection[0].Order) : Loc.F("Order in Layer {0}{1} ({2} elements)", (delta > 0 ? "+" : ""), delta, Selection.Count);
    }

    partial void OnPlacementTypeChanged(ElementType? value) => SceneInvalidated?.Invoke();

    /// <summary>The Scene drops its placement preview (its asset was deleted from the Project Window).</summary>
    public event Action? PlacementPreviewReset;

    /// <summary>Project Window items deleted: placement of a deleted asset stops, and its preview goes.</summary>
    private void OnProjectItemsDeleted()
    {
        if (PlacementType is { } type && RequiresAsset(type) && !ProjectWindow.HasPlaceableSelection)
            PlacementType = null;
        PlacementPreviewReset?.Invoke();
    }

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
        // Called for every drawn item: an index loop (foreach on an ObservableCollection allocates an enumerator).
        for (int i = 0; i < ElementTypes.Count; i++)
        {
            if (ElementTypes[i].Type == type)
                return ElementTypes[i].IsVisible;
        }
        return true;
    }

    // ================= TRIGGER EDITOR ================= //

    public void OpenTriggerEditor(TriggerElement trigger)
    {
        TriggerEditor?.Close();
        TriggerEditor = new TriggerEditorViewModel(this, trigger);
    }

    public void CloseTriggerEditor() => TriggerEditor = null;
}
