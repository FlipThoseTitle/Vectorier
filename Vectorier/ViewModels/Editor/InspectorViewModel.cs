using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Models.Scene;
using Vectorier.Services;
using Vectorier.Services.Projects;
using Vectorier.ViewModels.ProjectManager;

namespace Vectorier.ViewModels.Editor;

/// <summary>
/// The Inspector: transform fields shared by every element (Y and rotation shown with Y growing upward),
/// followed by the element's components. Edits go straight to the scene model.
/// </summary>
public sealed partial class InspectorViewModel : ObservableObject
{
    private readonly EditorViewModel _editor;
    private SceneElement? _target;

    public InspectorViewModel(EditorViewModel editor)
    {
        _editor = editor;
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditorViewModel.ActiveElement))
                SetTarget(editor.ActiveElement);
        };
        editor.SelectionChanged += () => OnPropertyChanged(nameof(SelectionNote));
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

    public EditorViewModel Editor => _editor;

    public SceneElement? Target => _target;

    public bool HasTarget => _target != null;

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
        if (ReferenceEquals(element, _target))
            return;
        TargetChanging?.Invoke();
        if (_target != null)
            _target.PropertyChanged -= OnTargetChanged;
        _target = element;
        if (_target != null)
            _target.PropertyChanged += OnTargetChanged;
        OnPropertyChanged(string.Empty);
        if (element is SpawnElement)
            _ = EnsureMovesAsync();
    }

    // ================= SPAWN ================= //

    private readonly Dictionary<string, string> _moveFiles = new(StringComparer.OrdinalIgnoreCase);
    private string? _movesProject;

    /// <summary>Moves of the project's moves.xml, for the Spawn animation dropdown.</summary>
    public IReadOnlyList<string> MoveNames { get; private set; } = Array.Empty<string>();

    private async System.Threading.Tasks.Task EnsureMovesAsync()
    {
        var project = _editor.Project;
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
            await AppServices.Dialogs.ShowMessageAsync("Spawn Animation", $"'{move}' has no FileName in moves.xml.", MessageKind.Warning);
            return;
        }
        var project = _editor.Project;
        var result = await ViewModels.Preview.CharacterPreviewViewModel.ShowAsync(project, $"Preview - {move} ({file})",
            new[] { Services.Assets.CharacterModel.DefaultSkin }, ViewModels.Preview.CharacterPreviewViewModel.AnimationPath(project, file),
            ViewModels.Preview.PreviewCamera.FollowCameraNode, spawn.StartFrame, showApply: true, applyText: "Set Start Frame");
        if (result != null && ReferenceEquals(_target, spawn))
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
            case nameof(ObjectElement.Reference): OnPropertyChanged(nameof(ReferenceText)); break;
            case nameof(PlatformElement.Surface): RaiseSurface(); break;
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

    public bool IsPlatform => _target is PlatformElement;
    private Services.Assets.PlatformSurfaceSettings? Surface => (_target as PlatformElement)?.Surface;
    public bool HasSurface => Surface != null;
    public bool CanAddSurface => _target is PlatformElement { Surface: null };

    /// <summary>Selected platforms that have a surface (edits apply to all of them, like Unity's multi-object editing).</summary>
    private List<PlatformElement> SurfaceTargets()
    {
        var list = _editor.Selection.OfType<PlatformElement>().Where(p => p.Surface != null).ToList();
        if (_target is PlatformElement { Surface: not null } t && !list.Contains(t))
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
    private async System.Threading.Tasks.Task RebuildSurface() => await _editor.RebuildSurfaceAsync(SurfaceTargets());

    [RelayCommand]
    private void RemoveSurface()
    {
        foreach (var platform in SurfaceTargets())
            _editor.RemoveSurface(platform);
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task AddSurface(Services.Assets.PlatformSurfaceType type)
    {
        if (_target is PlatformElement { Surface: null } platform)
            await _editor.AddSurfaceAsync(platform, type);
    }

    // ================= HEADER ================= //

    public string Header => _target switch
    {
        null => string.Empty,
        BuildingMarkerElement marker => marker.IsIn ? "Building In" : "Building Out",
        { Type: ElementType.Unknown } => $"<{_target.Name}>",
        _ => _target.Type.ToString(),
    };

    public StreamGeometry? Icon => _target == null ? null : ElementTypeInfo.GetIcon(_target.Type);

    public string SelectionNote => _editor.Selection.Count > 1 ? $"{_editor.Selection.Count} elements selected; showing the last one." : string.Empty;

    public bool NameEditable => _target?.NameEditable == true;

    public string NameTip => _target switch
    {
        AreaElement { IsTrick: true } => "A Trick Area is named after its trick's TriggerName in moves.xml (set automatically from the trick).",
        _ => "Image, Animation and Particle names are their texture: rename them in the Project Window.",
    };

    public string Name
    {
        get => _target?.Name ?? string.Empty;
        set
        {
            if (_target == null || !_target.NameEditable || _target.Name == value)
                return;
            if (_target is ObjectElement { Children.Count: > 0 } && _editor.ResolveDefinitionName(value) != null)
            {
                // Objects with a definition cannot have children: a group keeps its name.
                _editor.StatusText = $"'{value}' is an object definition; an Object with children cannot use its name.";
                OnPropertyChanged();
                return;
            }
            _target.Name = value;
        }
    }

    // ================= TRANSFORM ================= //

    public bool IsInteger => _target?.IntegerTransform == true;

    private double Snap(double value) => IsInteger ? Math.Round(value) : value;

    public double PosX
    {
        get => _target?.X ?? 0;
        set
        {
            if (_target == null || double.IsNaN(value)) return;
            _target.X = Snap(value);
        }
    }

    /// <summary>Displayed with Y growing upward (Vector's Y grows downward).</summary>
    public double PosY
    {
        get => -(_target?.Y ?? 0);
        set
        {
            if (_target == null || double.IsNaN(value)) return;
            _target.Y = Snap(-value);
        }
    }

    public bool CanRotate => _target is ImageElement;

    /// <summary>Counter-clockwise degrees (the XML matrix turns clockwise for positive angles).</summary>
    public double Rotation
    {
        get => _target is ImageElement image ? -image.Rotation : 0;
        set
        {
            if (_target is ImageElement image && !double.IsNaN(value))
                image.Rotation = -value;
        }
    }

    public bool CanResize => _target != null && _target.HasSize && _target.CanResize;

    // ================= TRICK AREA NAME ================= //

    public bool IsTrickArea => _target is AreaElement { IsTrick: true };

    public double Width
    {
        get => _target == null ? 0 : _target.HasSize ? _target.Width : _target.GetLocalBounds().Width;
        set
        {
            if (_target == null || !_target.HasSize || double.IsNaN(value)) return;
            value = Math.Max(IsInteger ? 1 : 0, Snap(value));
            if (_target is TrapezoidElement trapezoid)
            {
                trapezoid.Width = value;
                trapezoid.ApplySlopeAngle();
            }
            else
            {
                _target.Width = value;
            }
        }
    }

    public double Height
    {
        get => _target == null ? 0 : _target.HasSize ? _target.Height : _target.GetLocalBounds().Height;
        set
        {
            if (_target == null || !_target.HasSize || double.IsNaN(value)) return;
            value = Math.Max(IsInteger ? 1 : 0, Snap(value));
            if (_target is TrapezoidElement trapezoid)
            {
                // The slope keeps its angle: the height changes the rectangular base.
                double slope = trapezoid.SlopeHeight;
                trapezoid.BaseHeight = Math.Max(1, value - slope);
                trapezoid.Height = trapezoid.BaseHeight + slope;
            }
            else
            {
                _target.Height = value;
            }
        }
    }

    // ================= LAYER ================= //

    /// <summary>Layer applies to top-level elements; children inherit it from their root.</summary>
    public bool LayerEditable => _target != null && _target.LayerEditable && _target.Parent == null;

    public bool OrderEditable => _target != null && _target.LayerEditable;

    public string Layer
    {
        get => _target == null ? Layers.Default : _target.LayerEditable ? _target.EffectiveLayer : Layers.Default;
        set
        {
            if (_target == null || !LayerEditable || value == null || _target.Layer == value)
                return;
            _target.Layer = value;
        }
    }

    public int Order
    {
        get => _target?.Order ?? 0;
        set
        {
            if (_target == null || !OrderEditable) return;
            _target.Order = value;
        }
    }

    // ================= SELECTION (Common / Hunter mode) ================= //

    public bool SupportsSelection => _target != null && _target.Type != ElementType.Comment && _target.Type != ElementType.Unknown;

    public ModeSelection Selection
    {
        get => _target?.Selection ?? ModeSelection.All;
        set
        {
            if (_target == null || _target.Selection == value)
                return;
            if (_target is CameraElement camera && _editor.ValidateCameraSelection(camera, value) is { } error)
            {
                _ = AppServices.Dialogs.ShowMessageAsync("Camera", error, MessageKind.Warning);
                OnPropertyChanged();
                return;
            }
            _target.Selection = value;
        }
    }

    // ================= COMPONENT HELPERS ================= //

    public Color ImageColor
    {
        get => _target is ImageElement image ? image.Color : Colors.White;
        set
        {
            if (_target is ImageElement image && image.Color != value)
                image.Color = value;
        }
    }

    public string ReferenceText => _target is ObjectElement obj
        ? obj.Reference != null
            ? $"Content from {obj.Reference.SourceFile}"
            : string.IsNullOrEmpty(obj.Name) ? "Group (no definition)" : "No definition with this name in the level's Sets"
        : string.Empty;

    public int ChildCount => _target is ObjectElement obj ? obj.Children.Count : 0;

    public bool IsDefinedObject => _target is ObjectElement { Reference: not null };

    /// <summary>Trick Areas directly inside the selected Object (its children, then its definition's content).</summary>
    public IReadOnlyList<TrickAreaEntry> TrickAreas
    {
        get
        {
            var list = new List<TrickAreaEntry>();
            if (_target is not ObjectElement obj)
                return list;
            foreach (var area in obj.Children.OfType<AreaElement>().Where(a => a.AreaType == "Trick"))
                list.Add(new TrickAreaEntry(area.Name, area.ItemName, new MoveVisualizerViewModel.AreaTarget(area, area.ParentWorldPosition, obj, obj.Name)));
            if (obj.Reference is { } def)
            {
                _editor.BuildDefinition(def);
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
        if (_target is not AreaElement area)
            return;
        var shop = new ShopService(AppServices.FileSystem, _editor.Project);
        var localization = new LocalizationService(AppServices.FileSystem, _editor.Project);
        await shop.LoadAsync();
        await localization.LoadAsync();
        var tricks = TrickInfo.Load(_editor.Project, shop, localization);
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
        _editor.IsMoveVisualizerTab = true;
        await _editor.MoveVisualizer.PreviewAreaAsync(entry.Target);
    }

    [RelayCommand]
    private void OpenTriggerEditor()
    {
        if (_target is TriggerElement trigger)
            _editor.OpenTriggerEditor(trigger);
    }

    [RelayCommand]
    private void FrameSelected()
    {
        if (_target != null)
            _editor.RequestFrame(_target);
    }

    /// <summary>Spawn / Trick Area "Preview Animation": plays the move in the Move Visualizer.</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task PreviewMove()
    {
        switch (_target)
        {
            case SpawnElement spawn:
                _editor.IsMoveVisualizerTab = true;
                await _editor.MoveVisualizer.PreviewSpawnAsync(spawn);
                break;
            case AreaElement { AreaType: "Trick" } area:
                _editor.IsMoveVisualizerTab = true;
                await _editor.MoveVisualizer.PreviewAreaAsync(area);
                break;
        }
    }

    // ================= DYNAMIC ================= //

    /// <summary>The target's &lt;Transformation&gt;s, each with a Preview button.</summary>
    public IReadOnlyList<DynamicEntry> DynamicEntries => _target is { SupportsDynamics: true } t
        ? t.Dynamics.Select(d => new DynamicEntry(string.IsNullOrEmpty(d.Name) ? "Transform" : d.Name, $"{d.TotalFrames} frames")).ToList()
        : Array.Empty<DynamicEntry>();

    /// <summary>The Dynamic section is only shown when the element has transformations.</summary>
    public bool HasDynamics => _target is { SupportsDynamics: true } t && t.Dynamics.Count > 0;

    private void RaiseDynamics()
    {
        OnPropertyChanged(nameof(DynamicEntries));
        OnPropertyChanged(nameof(HasDynamics));
    }

    /// <summary>Opens the Dynamic Editor on this transformation and plays it from the start.</summary>
    [RelayCommand]
    private void PreviewDynamic(string? name)
    {
        if (_target is not { SupportsDynamics: true } target || string.IsNullOrEmpty(name))
            return;
        if (!target.IsSelected)
            _editor.Select(target);
        _editor.IsDynamicEditorTab = true;
        var dynamics = _editor.Dynamics;
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
        if (_target != null && _target.SupportsDynamics)
            _editor.IsDynamicEditorTab = true;
    }
}

public sealed record DynamicEntry(string Name, string Summary);

public sealed record TrickAreaEntry(string Name, string? Trick, MoveVisualizerViewModel.AreaTarget Target)
{
    public string TrickText => string.IsNullOrEmpty(Trick) ? "(no trick)" : Trick!;
}
