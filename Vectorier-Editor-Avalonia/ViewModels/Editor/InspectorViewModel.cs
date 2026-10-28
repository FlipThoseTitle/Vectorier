using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier_Editor_Avalonia.Models.Scene;
using Vectorier_Editor_Avalonia.Services;
using Vectorier_Editor_Avalonia.Services.Projects;
using Vectorier_Editor_Avalonia.ViewModels.ProjectManager;

namespace Vectorier_Editor_Avalonia.ViewModels.Editor;

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
            case nameof(AreaElement.AreaType):
                OnPropertyChanged(nameof(IsTrickArea));
                OnPropertyChanged(nameof(NameEditable));
                OnPropertyChanged(nameof(CanResize));
                OnPropertyChanged(nameof(TrickGroupNames));
                OnPropertyChanged(nameof(TrickName));
                OnPropertyChanged(nameof(Width));
                OnPropertyChanged(nameof(Height));
                break;
        }
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

    public string Name
    {
        get => _target?.Name ?? string.Empty;
        set
        {
            if (_target == null || !_target.NameEditable || _target.Name == value)
                return;
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

    private static List<string>? _trickGroupNames;
    private static string? _trickGroupProject;

    /// <summary>Names a Trick Area can have: the TriggerName / AreaName entries of moves.xml's TrickGroup.</summary>
    public IReadOnlyList<string> TrickGroupNames
    {
        get
        {
            if (_trickGroupProject != _editor.Project.Path)
            {
                _trickGroupProject = _editor.Project.Path;
                _trickGroupNames = null;
                _ = LoadTrickGroupNamesAsync();
            }
            var list = (_trickGroupNames ?? new List<string>()).ToList();
            if (_target is AreaElement { IsTrick: true } area && !string.IsNullOrEmpty(area.Name) && !list.Contains(area.Name))
                list.Insert(0, area.Name);
            return list;
        }
    }

    private async System.Threading.Tasks.Task LoadTrickGroupNamesAsync()
    {
        try
        {
            string path = Services.IO.VPath.Combine(_editor.Project.AnimationsFolder, "moves.xml");
            if (!await AppServices.FileSystem.FileExistsAsync(path))
                return;
            var doc = Services.Xml.XmlHelpers.ParseDocument(await AppServices.FileSystem.ReadAllBytesAsync(path));
            var group = doc.Root?.Element("ReactionGroups")?.Elements("ReactionGroup")
                .FirstOrDefault(g => string.Equals((string?)g.Attribute("Name"), "TrickGroup", StringComparison.OrdinalIgnoreCase));
            _trickGroupNames = group?.Elements()
                .SelectMany(e => new[] { (string?)e.Attribute("AreaName"), (string?)e.Attribute("TriggerName") })
                .Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            OnPropertyChanged(nameof(TrickGroupNames));
            OnPropertyChanged(nameof(TrickName));
        }
        catch
        {
            _trickGroupNames = new List<string>();
        }
    }

    public string? TrickName
    {
        get => _target is AreaElement { IsTrick: true } a ? a.Name : null;
        set
        {
            if (value != null && _target is AreaElement { IsTrick: true } a && a.Name != value)
                a.Name = value;
        }
    }

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
