using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Models.Scene;
using Vectorier.Services;
using Vectorier.Services.Localization;
using Vectorier.Services.Projects;
using Vectorier.ViewModels.ProjectManager;

namespace Vectorier.ViewModels.Editor;

/// <summary>
/// The Inspector: transform fields shared by every element (Y and rotation shown with Y growing upward),
/// followed by the element's components. Edits go straight to the scene model.
/// </summary>
public sealed partial class InspectorViewModel : ObservableObject
{
    public InspectorViewModel(EditorViewModel editor)
    {
        Editor = editor;
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditorViewModel.ActiveElement))
                SetTarget(editor.ActiveElement);
        };
        editor.SelectionChanged += () => { OnPropertyChanged(nameof(SelectionNote)); OnPropertyChanged(nameof(EditImageText)); };
        editor.StructureChanged += () =>
        {
            OnPropertyChanged(nameof(ChildCount));
            RaiseDynamics();
        };
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditorViewModel.IsDynamicEditorTab))
                RaiseDynamics();
        };
    }

    public EditorViewModel Editor { get; }

    public SceneElement? Target { get; private set; }

    public bool HasTarget => Target != null;

    public static IReadOnlyList<string> LayerNames => Layers.Names;
    public static IReadOnlyList<ModeSelection> SelectionModes { get; } = Enum.GetValues<ModeSelection>();
    public static IReadOnlyList<ImageType> ImageTypes { get; } = Enum.GetValues<ImageType>();
    public static IReadOnlyList<ImageDepth> ImageDepths { get; } = Enum.GetValues<ImageDepth>();
    public static IReadOnlyList<string> AreaTypes => AreaElement.KnownTypes;
    public static IReadOnlyList<ItemType> ItemTypes { get; } = Enum.GetValues<ItemType>();
    public static IReadOnlyList<ModelType> ModelTypes { get; } = Enum.GetValues<ModelType>();
    public static IReadOnlyList<int> AnimationTypes { get; } = new[] { 0, 1 };
    public static IReadOnlyList<int> ParticleTypes { get; } = new[] { 1, 2 };
    public static IReadOnlyList<int> TrapezoidTypes { get; } = new[] { 1, 2 };
    public static IReadOnlyList<string> HelpKeys { get; } = new[] { "Up", "Down", "Left", "Right" };

    /// <summary>Raised before the Inspector switches element: a field still being typed in is applied to the old one.</summary>
    public event Action? TargetChanging;

    private void SetTarget(SceneElement? element)
    {
        if (ReferenceEquals(element, Target))
            return;
        TargetChanging?.Invoke();
        if (Target != null)
            Target.PropertyChanged -= OnTargetChanged;
        Target = element;
        if (Target != null)
            Target.PropertyChanged += OnTargetChanged;
        OnPropertyChanged(string.Empty);
        if (element is SpawnElement)
            _ = EnsureMovesAsync();
        _ = RefreshTriggerAnimationAsync();
    }

    // ================= IMAGE ================= //

    /// <summary>"Edit Image", or "Merge &amp; Edit N Images" when several images are selected.</summary>
    public string EditImageText
    {
        get
        {
            int count = Editor.Selection.Count(e => e is ImageElement);
            return count > 1 ? Loc.F("Merge & Edit {0} Images", count) : Loc.T("Edit Image");
        }
    }

    [RelayCommand]
    private System.Threading.Tasks.Task EditImage() => Editor.Paint.EditSelectionAsync();

    // ================= SPAWN ================= //

    private readonly Dictionary<string, string> _moveFiles = new(StringComparer.OrdinalIgnoreCase);
    private string? _movesProject;

    /// <summary>Moves of the project's moves.xml, for the Spawn animation dropdown.</summary>
    public IReadOnlyList<string> MoveNames { get; private set; } = Array.Empty<string>();

    private async System.Threading.Tasks.Task EnsureMovesAsync()
    {
        var project = Editor.Project;
        if (_movesProject == project.Path)
            return;
        _movesProject = project.Path;
        var names = new List<string>();
        _moveFiles.Clear();
        try
        {
            var fs = AppServices.FileSystem;
            string path = Services.IO.VPath.Combine(project.AnimationsFolder, "moves.xml");
            if (await fs.FileExistsAsync(path))
            {
                var doc = Services.Xml.XmlHelpers.ParseDocument(await fs.ReadAllBytesAsync(path));
                if (doc.Root?.Element("Moves") is { } moves)
                    foreach (var move in moves.Elements())
                    {
                        string name = move.Name.LocalName;
                        if (!names.Contains(name))
                            names.Add(name);
                        if ((string?)move.Attribute("FileName") is { Length: > 0 } file)
                            _moveFiles.TryAdd(name, file);
                    }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[Inspector] moves.xml: " + ex.Message);
        }
        names.Sort(StringComparer.OrdinalIgnoreCase);
        MoveNames = names;
        OnPropertyChanged(nameof(MoveNames));
    }

    /// <summary>
    /// Opens the character preview window on the spawn's move; its timeline sets the start frame (Apply).
    /// </summary>
    public async System.Threading.Tasks.Task PickSpawnFrameAsync(SpawnElement spawn)
    {
        await EnsureMovesAsync();
        string move = spawn.MoveName;
        if (string.IsNullOrWhiteSpace(move))
            return;
        if (!_moveFiles.TryGetValue(move, out var file))
        {
            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Spawn Animation"), Loc.F("'{0}' has no FileName in moves.xml.", move), MessageKind.Warning);
            return;
        }
        var project = Editor.Project;
        var result = await ViewModels.Preview.CharacterPreviewViewModel.ShowAsync(project, Loc.F("Preview - {0} ({1})", move, file),
            new[] { Services.Assets.CharacterModel.DefaultSkin }, ViewModels.Preview.CharacterPreviewViewModel.AnimationPath(project, file), spawn.StartFrame, showApply: true, applyText: Loc.T("Set Start Frame"));
        if (result != null && ReferenceEquals(Target, spawn))
            spawn.StartFrame = result.StartFrame;
    }

    private void OnTargetChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SceneElement.X): OnPropertyChanged(nameof(PosX)); break;
            case nameof(SceneElement.Y): OnPropertyChanged(nameof(PosY)); break;
            case nameof(SceneElement.Width): OnPropertyChanged(nameof(Width)); break;
            case nameof(SceneElement.Height): OnPropertyChanged(nameof(Height)); break;
            case nameof(SceneElement.Name):
            case nameof(SceneElement.DisplayName):
                OnPropertyChanged(nameof(Name));
                OnPropertyChanged(nameof(Header));
                break;
            case nameof(SceneElement.Layer): OnPropertyChanged(nameof(Layer)); break;
            case nameof(SceneElement.Order): OnPropertyChanged(nameof(Order)); break;
            case nameof(SceneElement.Selection): OnPropertyChanged(nameof(Selection)); break;
            case nameof(ImageElement.Rotation): OnPropertyChanged(nameof(Rotation)); break;
            case nameof(ImageElement.Color): OnPropertyChanged(nameof(ImageColor)); break;
            case nameof(ObjectElement.Reference): OnPropertyChanged(nameof(ReferenceText)); OnPropertyChanged(nameof(HasReferenceText)); break;
            case nameof(PlatformElement.Surface): RaiseSurface(); break;
            case nameof(TriggerElement.ContentXml): _ = RefreshTriggerAnimationAsync(); break;
            case nameof(AreaElement.AreaType):
                OnPropertyChanged(nameof(IsTrickArea));
                OnPropertyChanged(nameof(NameEditable));
                OnPropertyChanged(nameof(NameTip));
                OnPropertyChanged(nameof(CanResize));
                OnPropertyChanged(nameof(Width));
                OnPropertyChanged(nameof(Height));
                break;
        }
    }

    // ================= PLATFORM SURFACE ================= //

    public static IReadOnlyList<Services.Assets.PlatformSurfaceType> SurfaceTypes { get; } = Enum.GetValues<Services.Assets.PlatformSurfaceType>();

    public bool IsPlatform => Target is PlatformElement;
    private Services.Assets.PlatformSurfaceSettings? Surface => (Target as PlatformElement)?.Surface;
    public bool HasSurface => Surface != null;
    public bool CanAddSurface => Target is PlatformElement { Surface: null };

    /// <summary>Selected platforms that have a surface (edits apply to all of them, like Unity's multi-object editing).</summary>
    private List<PlatformElement> SurfaceTargets()
    {
        var list = Editor.Selection.OfType<PlatformElement>().Where(p => p.Surface != null).ToList();
        if (Target is PlatformElement { Surface: not null } t && !list.Contains(t))
            list.Add(t);
        return list;
    }

    private void EditSurface(Func<Services.Assets.PlatformSurfaceSettings, Services.Assets.PlatformSurfaceSettings> change)
    {
        foreach (var platform in SurfaceTargets())
            platform.Surface = change(platform.Surface!);
    }

    public Services.Assets.PlatformSurfaceType SurfaceType
    {
        get => Surface?.Type ?? Services.Assets.PlatformSurfaceType.Rough;
        set { if (Surface is { } s && s.Type != value) EditSurface(x => x with { Type = value }); }
    }

    /// <summary>Block is only v_black: floor, walls and seed do not apply.</summary>
    public bool SurfaceHasEdges => Surface is { Type: not Services.Assets.PlatformSurfaceType.Block };

    /// <summary>Smooth never makes floor tiles.</summary>
    public bool SurfaceFloorEnabled => Surface is { Type: Services.Assets.PlatformSurfaceType.Rough };

    public bool SurfaceFloor
    {
        get => Surface?.Floor ?? false;
        set { if (Surface is { } s && s.Floor != value) EditSurface(x => x with { Floor = value }); }
    }

    public bool SurfaceLeftWall
    {
        get => Surface?.LeftWall ?? false;
        set { if (Surface is { } s && s.LeftWall != value) EditSurface(x => x with { LeftWall = value }); }
    }

    public bool SurfaceRightWall
    {
        get => Surface?.RightWall ?? false;
        set { if (Surface is { } s && s.RightWall != value) EditSurface(x => x with { RightWall = value }); }
    }

    public double SurfaceSeed
    {
        get => Surface?.Seed ?? 0;
        set
        {
            int seed = (int)Math.Clamp(Math.Round(value), 1, int.MaxValue);
            if (Surface is { } s && s.Seed != seed) EditSurface(x => x with { Seed = seed });
        }
    }

    private void RaiseSurface()
    {
        OnPropertyChanged(nameof(HasSurface));
        OnPropertyChanged(nameof(CanAddSurface));
        OnPropertyChanged(nameof(SurfaceType));
        OnPropertyChanged(nameof(SurfaceHasEdges));
        OnPropertyChanged(nameof(SurfaceFloorEnabled));
        OnPropertyChanged(nameof(SurfaceFloor));
        OnPropertyChanged(nameof(SurfaceLeftWall));
        OnPropertyChanged(nameof(SurfaceRightWall));
        OnPropertyChanged(nameof(SurfaceSeed));
    }

    /// <summary>A separate seed per platform, otherwise a multi-selection gets the same layout everywhere.</summary>
    [RelayCommand]
    private void RerollSurface()
    {
        foreach (var platform in SurfaceTargets())
            platform.Surface = platform.Surface! with { Seed = Services.Assets.PlatformSurfaceSettings.NewSeed() };
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task RebuildSurface() => await Editor.RebuildSurfaceAsync(SurfaceTargets());

    [RelayCommand]
    private void RemoveSurface()
    {
        foreach (var platform in SurfaceTargets())
            Editor.RemoveSurface(platform);
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task AddSurface(Services.Assets.PlatformSurfaceType type)
    {
        if (Target is PlatformElement { Surface: null } platform)
            await Editor.AddSurfaceAsync(platform, type);
    }

    // ================= HEADER ================= //

    public string Header => Target switch
    {
        null => string.Empty,
        BuildingMarkerElement marker => marker.IsIn ? Loc.T("Building In") : Loc.T("Building Out"),
        { Type: ElementType.Unknown } => $"<{Target.Name}>",
        _ => Target.Type.ToString(),
    };

    public StreamGeometry? Icon => Target == null ? null : ElementTypeInfo.GetIcon(Target.Type);

    public string SelectionNote => Editor.Selection.Count > 1 ? Loc.F("{0} elements selected; showing the last one.", Editor.Selection.Count) : string.Empty;

    public bool NameEditable => Target?.NameEditable == true;

    public string NameTip => Target switch
    {
        AreaElement { IsTrick: true } => Loc.T("Set from its trick when the trick changes; can be edited"),
        _ => Loc.T("Rename it in the Project Window"),
    };

    public string Name
    {
        get => Target?.Name ?? string.Empty;
        set
        {
            if (Target == null || !Target.NameEditable || Target.Name == value)
                return;
            if (Target is ObjectElement { Children.Count: > 0 } && Editor.ResolveDefinitionName(value) != null)
            {
                // Objects with a definition cannot have children: a group keeps its name.
                Editor.StatusText = Loc.F("'{0}' is an object definition; an Object with children cannot use its name.", value);
                OnPropertyChanged();
                return;
            }
            Target.Name = value;
        }
    }

    // ================= TRANSFORM ================= //

    public bool IsInteger => Target?.IntegerTransform == true;

    private double Snap(double value) => IsInteger ? Math.Round(value) : value;

    public double PosX
    {
        get => Target?.X ?? 0;
        set
        {
            if (Target == null || double.IsNaN(value)) return;
            Target.X = Snap(value);
        }
    }

    /// <summary>Displayed with Y growing upward (Vector's Y grows downward).</summary>
    public double PosY
    {
        get => -(Target?.Y ?? 0);
        set
        {
            if (Target == null || double.IsNaN(value)) return;
            Target.Y = Snap(-value);
        }
    }

    public bool CanRotate => Target is ImageElement;

    /// <summary>Counter-clockwise degrees (the XML matrix turns clockwise for positive angles).</summary>
    public double Rotation
    {
        get => Target is ImageElement image ? -image.Rotation : 0;
        set
        {
            if (Target is ImageElement image && !double.IsNaN(value))
                image.Rotation = -value;
        }
    }

    public bool CanResize => Target != null && Target.HasSize && Target.CanResize;

    // ================= TRICK AREA NAME ================= //

    public bool IsTrickArea => Target is AreaElement { IsTrick: true };

    public double Width
    {
        get => Target == null ? 0 : Target.HasSize ? Target.Width : Target.GetLocalBounds().Width;
        set
        {
            if (Target == null || !Target.HasSize || double.IsNaN(value)) return;
            value = Math.Max(IsInteger ? 1 : 0, Snap(value));
            if (Target is TrapezoidElement trapezoid)
            {
                trapezoid.Width = value;
                trapezoid.ApplySlopeAngle();
            }
            else
            {
                Target.Width = value;
            }
        }
    }

    public double Height
    {
        get => Target == null ? 0 : Target.HasSize ? Target.Height : Target.GetLocalBounds().Height;
        set
        {
            if (Target == null || !Target.HasSize || double.IsNaN(value)) return;
            value = Math.Max(IsInteger ? 1 : 0, Snap(value));
            if (Target is TrapezoidElement trapezoid)
            {
                // The slope keeps its angle: the height changes the rectangular base.
                double slope = trapezoid.SlopeHeight;
                trapezoid.BaseHeight = Math.Max(1, value - slope);
                trapezoid.Height = trapezoid.BaseHeight + slope;
            }
            else
            {
                Target.Height = value;
            }
        }
    }

    // ================= LAYER ================= //

    /// <summary>Layer applies to top-level elements; children inherit it from their root.</summary>
    public bool LayerEditable => Target != null && Target.LayerEditable && Target.Parent == null;

    public bool OrderEditable => Target != null && Target.LayerEditable;

    public string Layer
    {
        get => Target == null ? Layers.Default : Target.LayerEditable ? Target.EffectiveLayer : Layers.Default;
        set
        {
            if (Target == null || !LayerEditable || value == null || Target.Layer == value)
                return;
            Target.Layer = value;
        }
    }

    public int Order
    {
        get => Target?.Order ?? 0;
        set
        {
            if (Target == null || !OrderEditable) return;
            Target.Order = value;
        }
    }

    // ================= SELECTION (Common / Hunter mode) ================= //

    public bool SupportsSelection => Target != null && Target.Type != ElementType.Comment && Target.Type != ElementType.Unknown;

    public ModeSelection Selection
    {
        get => Target?.Selection ?? ModeSelection.All;
        set
        {
            if (Target == null || Target.Selection == value)
                return;
            if (Target is CameraElement camera && Editor.ValidateCameraSelection(camera, value) is { } error)
            {
                _ = AppServices.Dialogs.ShowMessageAsync(Loc.T("Camera"), error, MessageKind.Warning);
                OnPropertyChanged();
                return;
            }
            Target.Selection = value;
        }
    }

    // ================= COMPONENT HELPERS ================= //

    public Color ImageColor
    {
        get => Target is ImageElement image ? image.Color : Colors.White;
        set
        {
            if (Target is ImageElement image && image.Color != value)
                image.Color = value;
        }
    }

    public string ReferenceText => Target is ObjectElement obj
        ? obj.Reference != null
            ? Loc.F("From {0}", obj.Reference.SourceFile)
            : string.IsNullOrEmpty(obj.Name) ? string.Empty : Loc.T("No definition with this name in the level's Sets")
        : string.Empty;

    public bool HasReferenceText => ReferenceText.Length > 0;

    public int ChildCount => Target is ObjectElement obj ? obj.Children.Count : 0;

    /// <summary>Trick Areas directly inside the selected Object (its children, then its definition's content).</summary>
    public IReadOnlyList<TrickAreaEntry> TrickAreas
    {
        get
        {
            var list = new List<TrickAreaEntry>();
            if (Target is not ObjectElement obj)
                return list;
            foreach (var area in obj.Children.OfType<AreaElement>().Where(a => a.AreaType == "Trick"))
                list.Add(new TrickAreaEntry(area.Name, area.ItemName, new MoveVisualizerViewModel.AreaTarget(area, area.ParentWorldPosition, obj, obj.Name)));
            if (obj.Reference is { } def)
            {
                Editor.BuildDefinition(def);
                var w = obj.WorldPosition;
                foreach (var area in def.Content.OfType<AreaElement>().Where(a => a.AreaType == "Trick"))
                    list.Add(new TrickAreaEntry(area.Name, area.ItemName, new MoveVisualizerViewModel.AreaTarget(area, w, obj, obj.Name)));
            }
            return list;
        }
    }

    public bool HasTrickAreas => TrickAreas.Count > 0;

    /// <summary>Trick Area: picks its trick in the trick grid (one trick per Area), applied right away.</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task SelectTrickAsync()
    {
        if (Target is not AreaElement area)
            return;
        var shop = new ShopService(AppServices.FileSystem, Editor.Project);
        var localization = new LocalizationService(AppServices.FileSystem, Editor.Project);
        await shop.LoadAsync();
        await localization.LoadAsync();
        var tricks = TrickInfo.Load(Editor.Project, shop, localization);
        var dialog = new TrickPickerDialogViewModel(tricks, area.ItemName, name =>
        {
            area.ItemName = name ?? string.Empty;
        });
        await AppServices.Dialogs.ShowAsync(dialog);
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task PreviewTrickAreaAsync(TrickAreaEntry? entry)
    {
        if (entry == null)
            return;
        var mode = await AskPreviewModeAsync();
        if (mode == null)
            return;
        Editor.IsMoveVisualizerTab = true;
        if (mode == MoveSpawnMode.Gameplay)
            await Editor.MoveVisualizer.PreviewAreaGameplayAsync(entry.Target);
        else
            await Editor.MoveVisualizer.PreviewAreaAsync(entry.Target);
    }

    /// <summary>
    /// "Preview Animation" on a Spawn or an Area asks how to preview it: play the move (Animation) or spawn a
    /// runner to play with the arrow keys (Gameplay). Null when cancelled. The tutorial always uses Animation.
    /// </summary>
    private static async System.Threading.Tasks.Task<MoveSpawnMode?> AskPreviewModeAsync()
    {
        if (Services.Tutorial.TutorialGuide.IsActive)
            return MoveSpawnMode.Animation;
        var dialog = new ViewModels.Dialogs.MessageDialogViewModel(Loc.T("Preview Animation"),
            Loc.T("How do you want to preview it?\n\nAnimation plays the move. Gameplay spawns a runner you control with the arrow keys."),
            MessageKind.Question);
        dialog.Buttons.Add(new ViewModels.Dialogs.DialogButton(Loc.T("Animation"), MoveSpawnMode.Animation, isDefault: true));
        dialog.Buttons.Add(new ViewModels.Dialogs.DialogButton(Loc.T("Gameplay"), MoveSpawnMode.Gameplay));
        dialog.Buttons.Add(new ViewModels.Dialogs.DialogButton(Loc.T("Cancel"), null, isCancel: true));
        return await AppServices.Dialogs.ShowAsync(dialog) is MoveSpawnMode mode ? mode : null;
    }

    // ================= TRIGGER: PREVIEW ANIMATION ================= //

    /// <summary>The selected Trigger runs a Force Animation (its own loops or its template's): "Preview Animation" is shown.</summary>
    [ObservableProperty] private bool _canPreviewTriggerAnimation;

    private int _triggerAnimationCheck;

    private async System.Threading.Tasks.Task RefreshTriggerAnimationAsync()
    {
        int check = ++_triggerAnimationCheck;
        if (Target is not TriggerElement trigger)
        {
            CanPreviewTriggerAnimation = false;
            return;
        }
        bool found;
        try
        {
            found = (await Editor.MoveVisualizer.FindTriggerAnimationsAsync(trigger)).Count > 0;
        }
        catch
        {
            found = false;
        }
        if (check == _triggerAnimationCheck)
            CanPreviewTriggerAnimation = found;
    }

    /// <summary>Plays the trigger's Force Animation in the Move Visualizer, placed on the trigger.</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task PreviewTriggerAnimation()
    {
        if (Target is not TriggerElement trigger)
            return;
        Editor.IsMoveVisualizerTab = true;
        await Editor.MoveVisualizer.PreviewTriggerAsync(trigger);
    }

    [RelayCommand]
    private void OpenTriggerEditor()
    {
        if (Target is TriggerElement trigger)
            Editor.OpenTriggerEditor(trigger);
    }

    [RelayCommand]
    private void FrameSelected()
    {
        if (Target != null)
            Editor.RequestFrame(Target);
    }

    /// <summary>Spawn / Trick Area "Preview Animation": plays the move in the Move Visualizer.</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task PreviewMove()
    {
        if (Target is not (SpawnElement or AreaElement { AreaType: "Trick" }))
            return;
        var target = Target;
        var mode = await AskPreviewModeAsync();
        if (mode == null)
            return;
        bool gameplay = mode == MoveSpawnMode.Gameplay;
        switch (target)
        {
            case SpawnElement spawn:
                Editor.IsMoveVisualizerTab = true;
                if (gameplay)
                    await Editor.MoveVisualizer.PreviewSpawnGameplayAsync(spawn);
                else
                    await Editor.MoveVisualizer.PreviewSpawnAsync(spawn);
                break;
            case AreaElement area:
                Editor.IsMoveVisualizerTab = true;
                if (gameplay)
                    await Editor.MoveVisualizer.PreviewAreaGameplayAsync(area);
                else
                    await Editor.MoveVisualizer.PreviewAreaAsync(area);
                break;
        }
    }

    // ================= DYNAMIC ================= //

    /// <summary>The target's &lt;Transformation&gt;s, each with a Preview button.</summary>
    public IReadOnlyList<DynamicEntry> DynamicEntries => Target is { SupportsDynamics: true } t
        ? t.Dynamics.Select(d => new DynamicEntry(string.IsNullOrEmpty(d.Name) ? "Transform" : d.Name, Loc.F("{0} frames", d.TotalFrames)))
            .Concat(t.InheritedDynamics.Select(d => new DynamicEntry(string.IsNullOrEmpty(d.Name) ? "Transform" : d.Name,
                Loc.F("{0} frames, from the object", d.TotalFrames)))).ToList()
        : Array.Empty<DynamicEntry>();

    /// <summary>The Dynamic section is only shown when the element has transformations.</summary>
    public bool HasDynamics => Target is { SupportsDynamics: true } t && (t.Dynamics.Count > 0 || t.InheritedDynamics.Count > 0);

    private void RaiseDynamics()
    {
        OnPropertyChanged(nameof(DynamicEntries));
        OnPropertyChanged(nameof(HasDynamics));
    }

    /// <summary>Opens the Dynamic Editor on this transformation and plays it from the start.</summary>
    [RelayCommand]
    private void PreviewDynamic(string? name)
    {
        if (Target is not { SupportsDynamics: true } target || string.IsNullOrEmpty(name))
            return;
        if (!target.IsSelected)
            Editor.Select(target);
        Editor.IsDynamicEditorTab = true;
        var dynamics = Editor.Dynamics;
        dynamics.BindSelection();
        var track = dynamics.Tracks.FirstOrDefault(t => ReferenceEquals(t.Element, target));
        if (track == null)
            return;
        dynamics.SelectTransformation(track, name);
        dynamics.Frame = 0;
        if (!dynamics.IsPlaying)
            dynamics.TogglePlay();
    }

    [RelayCommand]
    private void EditDynamics()
    {
        if (Target != null && Target.SupportsDynamics)
            Editor.IsDynamicEditorTab = true;
    }
}

public sealed record DynamicEntry(string Name, string Summary);

public sealed record TrickAreaEntry(string Name, string? Trick, MoveVisualizerViewModel.AreaTarget Target)
{
    public string TrickText => string.IsNullOrEmpty(Trick) ? Loc.T("(no trick)") : Trick!;
}
