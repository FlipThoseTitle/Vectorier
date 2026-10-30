using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Models.Scene;
using Vectorier.Services;
using Vectorier.Services.Assets;
using Vectorier.Services.IO;
using Vectorier.Services.MoveVisualizer;
using Vectorier.Services.Xml;

namespace Vectorier.ViewModels.Editor;

public enum MovePlacementMode { Movement, Tricks, Custom }

public enum MovementType
{
    Jump,
    JumpOffFly,
    Run,
    RunFast,
    RunFastJump,
    RunFastJumpOff,
    Slide,
    SlideOff,
    FastSlide,
    DivingKongToFly,
    SpeedVaultToFly,
    CollisionToFly,
    FlyCollision,
}

/// <summary>A trick of the active project's TrickGroup whose bin exists.</summary>
public sealed partial class TrickEntry : ObservableObject
{
    public TrickEntry(string identifier, string fileName)
    {
        Identifier = identifier;
        FileName = fileName;
        string[] parts = Path.GetFileNameWithoutExtension(fileName).Split('_', StringSplitOptions.RemoveEmptyEntries);
        DisplayName = string.Join(" ", parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
        ImageName = "TRACK_TRICK_" + Path.GetFileNameWithoutExtension(fileName).Replace("_", string.Empty).ToUpperInvariant();
    }

    public string Identifier { get; }
    public string FileName { get; }
    public string DisplayName { get; }
    public string ImageName { get; }

    [ObservableProperty] private Bitmap? _icon;
    public bool HasIcon => Icon != null;
    partial void OnIconChanged(Bitmap? value) => OnPropertyChanged(nameof(HasIcon));
}

/// <summary>Where the placement preview snapped to (drawn as a line and a dot in the Scene).</summary>
public readonly record struct MoveSnapIndicator(Point Cursor, Point Target, bool IsCorner);

/// <summary>
/// Move Visualizer (port of Unity's EditorScripts/Tools/MoveVisualizer.cs): places the runner model in the
/// Scene and plays a move on it, to check jump distances and trick timing while building the level.
/// The character is always drawn as a black silhouette, and stays in the Scene after placement stops.
/// </summary>
public sealed partial class MoveVisualizerViewModel : ObservableObject
{
    public const string ModelXmlPath = "Editor/MoveVisualizer/runner.xml";
    public const string MovementFolder = "Editor/MoveVisualizer/animations";
    private const string MovesRelativePath = "animations/moves.xml";
    private const string BinsRelativeFolder = "animations/data";
    private const string TrickIconsRelativeFolder = "icons/tricks";
    private const string DefaultProject = "Vector";

    // Platform snapping while C is held (Unity TransformSnap constants).
    private const double VertexSnapRadius = 50;
    private const double BoundsSearchThreshold = 200;

    public static readonly Dictionary<MovementType, string> MovementBins = new()
    {
        { MovementType.Jump, "fly.bin" },
        { MovementType.JumpOffFly, "jump_off_fly.bin" },
        { MovementType.Run, "run.bin" },
        { MovementType.RunFast, "run_fast_from_run.bin" },
        { MovementType.RunFastJump, "run_fast_fly.bin" },
        { MovementType.RunFastJumpOff, "run_fast_jump_off.bin" },
        { MovementType.Slide, "slide_simple.bin" },
        { MovementType.SlideOff, "slide_simple_and_fall.bin" },
        { MovementType.FastSlide, "fast_slide_simple.bin" },
        { MovementType.DivingKongToFly, "diving_kong_to_fly.bin" },
        { MovementType.SpeedVaultToFly, "speed_vault_fly.bin" },
        { MovementType.CollisionToFly, "collision_to_fly.bin" },
        { MovementType.FlyCollision, "fly_collision.bin" },
    };

    public static IReadOnlyList<MovePlacementMode> PlacementModes { get; } = Enum.GetValues<MovePlacementMode>();
    public static IReadOnlyList<MovementType> MovementTypes { get; } = Enum.GetValues<MovementType>();
    public static IReadOnlyList<string> PivotNodePresets { get; } = new[] { "NPivot", "DetectorH", "DetectorV" };

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static double Now => Clock.Elapsed.TotalSeconds;

    private readonly EditorViewModel _editor;
    private readonly DispatcherTimer _timer;
    private MoveModel? _model;
    private Task<MoveModel?>? _modelLoad;
    private byte[]? _customBinData;
    private string? _customBinName;
    private Vector3 _lastPlacedPosition; // animation space
    private bool _syncingFrame;

    public MoveVisualizerViewModel(EditorViewModel editor)
    {
        _editor = editor;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1.0 / MoveAnimation.TargetFps), DispatcherPriority.Render, (_, _) => UpdatePlayback());
        editor.SelectionChanged += RefreshSelectedArea;
    }

    public MoveAnimation Animation { get; } = new();

    /// <summary>Element the placed animation follows (Unity's placement host transform): the Spawn / Area's parent.</summary>
    public SceneElement? Host { get; private set; }

    /// <summary>Raised when the Scene must redraw the character.</summary>
    public event Action? Changed;

    /// <summary>Follow Camera: the Scene centers on this level point.</summary>
    public event Action<Point>? FollowRequested;

    // ================= SETTINGS ================= //

    [ObservableProperty] private bool _renderModel = true;
    [ObservableProperty] private bool _renderEdges;
    [ObservableProperty] private bool _renderTrajectory = true;
    [ObservableProperty] private bool _renderDetector;
    [ObservableProperty] private int _deltaDetectorH;
    [ObservableProperty] private int _deltaDetectorV;
    [ObservableProperty] private bool _followCamera;
    [ObservableProperty] private bool _reverse;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMovementMode), nameof(IsTricksMode), nameof(IsCustomMode))]
    private MovePlacementMode _placementMode = MovePlacementMode.Movement;

    [ObservableProperty] private MovementType _movementType = MovementType.Jump;
    [ObservableProperty] private TrickEntry? _selectedTrick;
    [ObservableProperty] private string _customBinPath = string.Empty;
    [ObservableProperty] private string _pivotNodeName = "NPivot";
    [ObservableProperty] private bool _stayInPlace;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MainButtonText))]
    private bool _isPlacementActive;

    [ObservableProperty] private bool _isPlaced;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public bool IsMovementMode => PlacementMode == MovePlacementMode.Movement;
    public bool IsTricksMode => PlacementMode == MovePlacementMode.Tricks;
    public bool IsCustomMode => PlacementMode == MovePlacementMode.Custom;

    public ObservableCollection<TrickEntry> Tricks { get; } = new();
    [ObservableProperty] private bool _tricksLoaded;
    public bool HasTricks => Tricks.Count > 0;

    /// <summary>Hold C while placing: the pivot sticks to the nearest platform corner or edge.</summary>
    public bool PlatformSnapHeld { get; set; }

    public MoveSnapIndicator? SnapIndicator { get; private set; }

    // ================= SELECTED TRICK AREA ================= //

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTrickArea), nameof(MainButtonText), nameof(SelectedAreaName))]
    private AreaElement? _selectedArea;

    public bool HasSelectedTrickArea => SelectedArea is { AreaType: "Trick" };
    public string SelectedAreaName => SelectedArea?.Name ?? string.Empty;

    public string MainButtonText => HasSelectedTrickArea ? "Preview Animation" : IsPlacementActive ? "Stop Placement" : "Start Placement";

    /// <summary>An Area to preview, with where its parent sits (it may come from an Object's referenced content).</summary>
    public sealed record AreaTarget(AreaElement Area, Point Origin, SceneElement Host, string? ParentName);

    private AreaTarget? _selectedTarget;

    /// <summary>
    /// Unity RefreshSelectedAreaComponent: the selected Area, or a same-named Area inside the selected Object
    /// (its own children first, then the content it references from the level's Sets).
    /// </summary>
    private void RefreshSelectedArea()
    {
        _selectedTarget = null;
        switch (_editor.ActiveElement)
        {
            case AreaElement area:
                _selectedTarget = TargetFor(area);
                break;
            case ObjectElement obj:
            {
                var inline = obj.Children.SelectMany(SceneDocument.Walk).OfType<AreaElement>().FirstOrDefault(a => a.Name == obj.Name);
                if (inline != null)
                {
                    _selectedTarget = TargetFor(inline);
                }
                else if (obj.Reference is { } def)
                {
                    var referenced = def.Content.SelectMany(SceneDocument.Walk).OfType<AreaElement>().FirstOrDefault(a => a.Name == obj.Name);
                    if (referenced != null)
                    {
                        var w = obj.WorldPosition;
                        var inner = referenced.ParentWorldPosition;
                        _selectedTarget = new AreaTarget(referenced, new Point(w.X + inner.X, w.Y + inner.Y), referenced.Parent ?? (SceneElement)obj,
                            referenced.Parent?.Name ?? obj.Name);
                    }
                }
                break;
            }
        }
        var area0 = _selectedTarget?.Area;
        if (!ReferenceEquals(area0, SelectedArea))
            SelectedArea = area0;
        else
            OnPropertyChanged(nameof(HasSelectedTrickArea));
    }

    private static AreaTarget TargetFor(AreaElement area) =>
        new(area, area.ParentWorldPosition, (SceneElement?)area.Parent ?? area, area.Parent?.Name);

    // ================= FRAMES ================= //

    [ObservableProperty] private int _frame;
    [ObservableProperty] private int _startFrame;
    [ObservableProperty] private int _endFrame;
    [ObservableProperty] private int _maxFrame;
    [ObservableProperty] private bool _hasFrames;
    [ObservableProperty] private bool _isPlaying = true;

    public string PlayButtonText => IsPlaying ? "Pause" : "Play";

    partial void OnIsPlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(PlayButtonText));
        if (_syncingFrame) return;
        Animation.IsPlaying = value;
        UpdateTimer();
    }

    partial void OnFrameChanged(int value)
    {
        if (_syncingFrame || !HasFrames) return;
        // Scrubbing pauses playback (Unity IntSlider).
        IsPlaying = false;
        Animation.CurrentFrameIndex = value;
        Animation.ApplyFrame(value);
        AfterFrameApplied();
    }

    partial void OnStartFrameChanged(int value)
    {
        if (_syncingFrame) return;
        Animation.StartFrame = value;
        ClampFrames();
    }

    partial void OnEndFrameChanged(int value)
    {
        if (_syncingFrame) return;
        Animation.EndFrame = value;
        ClampFrames();
    }

    /// <summary>Start in [0, max], End in [Start, max], current frame inside.</summary>
    private void ClampFrames()
    {
        int max = Math.Max(0, Animation.AnimationFrames.Count - 1);
        Animation.StartFrame = Math.Clamp(Animation.StartFrame, 0, max);
        if (Animation.EndFrame > max) Animation.EndFrame = max;
        if (Animation.EndFrame < Animation.StartFrame) Animation.EndFrame = Animation.StartFrame;
        if (Animation.CurrentFrameIndex < Animation.StartFrame || Animation.CurrentFrameIndex > Animation.EndFrame)
        {
            Animation.CurrentFrameIndex = Math.Clamp(Animation.CurrentFrameIndex, Animation.StartFrame, Animation.EndFrame);
            Animation.ApplyFrame(Animation.CurrentFrameIndex);
            Changed?.Invoke();
        }
        SyncFrames();
    }

    private void SyncFrames()
    {
        _syncingFrame = true;
        HasFrames = Animation.AnimationFrames.Count > 0;
        MaxFrame = Math.Max(0, Animation.AnimationFrames.Count - 1);
        StartFrame = Animation.StartFrame;
        EndFrame = HasFrames ? Animation.EndFrame : 0;
        Frame = Animation.CurrentFrameIndex;
        IsPlaying = Animation.IsPlaying;
        _syncingFrame = false;
    }

    // ================= SETTING CHANGES ================= //

    partial void OnRenderModelChanged(bool value) => Changed?.Invoke();
    partial void OnRenderEdgesChanged(bool value) => Changed?.Invoke();
    partial void OnRenderTrajectoryChanged(bool value) => Changed?.Invoke();
    partial void OnRenderDetectorChanged(bool value) => Changed?.Invoke();
    partial void OnDeltaDetectorHChanged(int value)
    {
        if (value < 0) DeltaDetectorH = 0;
        Changed?.Invoke();
    }
    partial void OnDeltaDetectorVChanged(int value)
    {
        if (value < 0) DeltaDetectorV = 0;
        Changed?.Invoke();
    }
    partial void OnFollowCameraChanged(bool value) => Follow();

    partial void OnPlacementModeChanged(MovePlacementMode value)
    {
        if (value == MovePlacementMode.Tricks && !TricksLoaded)
            _ = DiscoverTricksAsync();
    }

    partial void OnCustomBinPathChanged(string value)
    {
        if (value != _customBinName)
        {
            _customBinData = null;
            _customBinName = null;
        }
    }

    async partial void OnReverseChanged(bool value)
    {
        Animation.Reverse = value;
        if (_model != null)
            Animation.EnsureModelLoadedForPreview(_model);
        if (!IsPlaced)
        {
            Changed?.Invoke();
            return;
        }
        // Re-place at the tracked location so the reversed axis shows right away.
        if (HasSelectedTrickArea && _selectedTarget != null)
            await PreviewAreaAsync(_selectedTarget);
        else
            await PlaceAtAsync(_lastPlacedPosition, Host);
    }

    // ================= COMMANDS ================= //

    [RelayCommand]
    private async Task MainButton()
    {
        if (HasSelectedTrickArea && _selectedTarget != null)
        {
            await PreviewAreaAsync(_selectedTarget);
            return;
        }
        IsPlacementActive = !IsPlacementActive;
        PlatformSnapHeld = false;
        SnapIndicator = null;
        if (IsPlacementActive)
        {
            if (await EnsureModelAsync() == null)
                IsPlacementActive = false;
            else
                StatusMessage = "Click in the Scene to place the runner. Hold C to stick to platforms.";
        }
        else
        {
            Animation.ClearPreview();
            StatusMessage = string.Empty;
        }
        Changed?.Invoke();
    }

    /// <summary>Stops placing (Esc in the Scene); the placed character stays.</summary>
    public void StopPlacement()
    {
        if (!IsPlacementActive) return;
        IsPlacementActive = false;
        PlatformSnapHeld = false;
        SnapIndicator = null;
        Animation.ClearPreview();
        StatusMessage = string.Empty;
        Changed?.Invoke();
    }

    [RelayCommand]
    private void Clear()
    {
        ResetAll();
        StatusMessage = string.Empty;
        Changed?.Invoke();
    }

    [RelayCommand]
    private void PlayPause()
    {
        IsPlaying = !IsPlaying;
    }

    [RelayCommand]
    private void Restart()
    {
        if (!HasFrames) return;
        Animation.CurrentFrameIndex = Animation.StartFrame;
        Animation.ApplyFrame(Animation.StartFrame);
        AfterFrameApplied();
    }

    [RelayCommand]
    private void SetPivot(string? preset)
    {
        if (!string.IsNullOrEmpty(preset))
            PivotNodeName = preset;
    }

    [RelayCommand]
    private void PickTrick(TrickEntry? trick)
    {
        if (trick != null)
            SelectedTrick = trick;
    }

    [RelayCommand]
    private Task RefreshTricks() => DiscoverTricksAsync();

    /// <summary>"..." next to Bin Path: any .bin / .bytes file (read once, so it also works in the browser).</summary>
    [RelayCommand]
    private async Task BrowseCustomBin()
    {
        var topLevel = AppServices.Shell.CurrentTopLevel;
        if (topLevel == null) return;
        var start = await AppServices.FileSystem.GetStorageFolderAsync(_editor.Project.AnimationsFolder, topLevel.StorageProvider);
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select animation bin",
            AllowMultiple = false,
            SuggestedStartLocation = start,
            FileTypeFilter = new[] { new FilePickerFileType("Animation bin") { Patterns = new[] { "*.bin", "*.bytes" } } },
        });
        if (files.Count == 0) return;
        try
        {
            await using var stream = await files[0].OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            _customBinData = ms.ToArray();
            _customBinName = files[0].TryGetLocalPath() ?? files[0].Name;
            CustomBinPath = _customBinName;
        }
        catch (Exception ex)
        {
            Report("Could not read the bin: " + ex.Message);
        }
    }

    // ================= SCENE INPUT ================= //

    /// <summary>Pointer moved over the Scene while placing: the preview pose follows (pivot at the cursor).</summary>
    public void UpdatePlacementPreview(Point level)
    {
        if (!IsPlacementActive || _model == null) return;
        Animation.EnsureModelLoadedForPreview(_model);
        var target = level;
        SnapIndicator = null;
        if (PlatformSnapHeld && TryGetPlatformSnapPoint(level, out var snapped, out bool corner))
        {
            SnapIndicator = new MoveSnapIndicator(level, snapped, corner);
            target = snapped;
        }
        try
        {
            Animation.UpdatePreview(ToSpace(target, null), PivotNodeName);
        }
        catch (Exception ex)
        {
            Report(ex.Message);
        }
        Changed?.Invoke();
    }

    /// <summary>Left click while placing: the move starts with its pivot at the (snapped) cursor.</summary>
    public async Task PlaceFromSceneAsync(Point level)
    {
        if (!IsPlacementActive) return;
        var target = level;
        if (PlatformSnapHeld && TryGetPlatformSnapPoint(level, out var snapped, out _))
            target = snapped;
        if (await PlaceAtAsync(ToSpace(target, null), null))
        {
            Animation.ClearPreview();
            Changed?.Invoke();
        }
    }

    /// <summary>Unity TransformSnap.TryGetPlatformSnapPoint over the level's Platforms.</summary>
    private bool TryGetPlatformSnapPoint(Point point, out Point snapped, out bool isCorner)
    {
        snapped = point;
        isCorner = false;
        Point bestCorner = default, bestEdge = default;
        double bestCornerDist = double.MaxValue, bestEdgeDist = double.MaxValue;

        if (!_editor.IsTypeVisible(ElementType.Platform))
            return false;
        foreach (var r in PlatformRects())
        {
            double dx = Math.Max(Math.Max(r.Left - point.X, 0), point.X - r.Right);
            double dy = Math.Max(Math.Max(r.Top - point.Y, 0), point.Y - r.Bottom);
            if (dx * dx + dy * dy > BoundsSearchThreshold * BoundsSearchThreshold) continue;

            var corners = new[] { r.BottomLeft, r.BottomRight, r.TopLeft, r.TopRight };
            foreach (var c in corners)
            {
                double d = Distance(point, c);
                if (d < bestCornerDist) { bestCornerDist = d; bestCorner = c; }
            }
            foreach (var p in new[]
                     {
                         ClosestOnSegment(corners[0], corners[1], point), ClosestOnSegment(corners[1], corners[3], point),
                         ClosestOnSegment(corners[3], corners[2], point), ClosestOnSegment(corners[2], corners[0], point),
                     })
            {
                double d = Distance(point, p);
                if (d < bestEdgeDist) { bestEdgeDist = d; bestEdge = p; }
            }
        }

        if (bestCornerDist <= VertexSnapRadius)
        {
            snapped = bestCorner;
            isCorner = true;
            return true;
        }
        if (bestEdgeDist <= BoundsSearchThreshold)
        {
            snapped = bestEdge;
            return true;
        }
        return false;
    }

    /// <summary>Level bounds of every Platform, including those inside Objects' referenced content.</summary>
    private IEnumerable<Rect> PlatformRects()
    {
        var results = new List<Rect>();
        void Visit(SceneElement e, Avalonia.Vector parentWorld, int depth)
        {
            if (depth > 48) return;
            if (e is PlatformElement)
            {
                results.Add(e.GetLocalBounds().Translate(parentWorld));
                return;
            }
            if (e is not ObjectElement obj) return;
            var inner = parentWorld + new Avalonia.Vector(obj.X, obj.Y);
            if (obj.Reference is { } def)
            {
                _editor.BuildDefinition(def);
                foreach (var c in def.Content) Visit(c, inner, depth + 1);
            }
            foreach (var c in obj.Children) Visit(c, inner, depth + 1);
        }
        foreach (var root in _editor.Document.Roots)
            Visit(root, default, 0);
        return results;
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static Point ClosestOnSegment(Point a, Point b, Point p)
    {
        var ab = b - a;
        double len2 = ab.X * ab.X + ab.Y * ab.Y;
        if (len2 < 1e-9) return a;
        double t = Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / len2, 0, 1);
        return new Point(a.X + ab.X * t, a.Y + ab.Y * t);
    }

    // ================= COORDINATES ================= //

    /// <summary>Host position in Unity-like space (Y up).</summary>
    private static Vector3 HostOffset(SceneElement? host)
    {
        if (host == null) return Vector3.Zero;
        var p = host.WorldPosition;
        return new Vector3((float)p.X, (float)-p.Y, 0);
    }

    /// <summary>Level point (Y down) → animation space (Y up, relative to the host).</summary>
    public static Vector3 ToSpace(Point level, SceneElement? host) => new Vector3((float)level.X, (float)-level.Y, 0) - HostOffset(host);

    /// <summary>Animation-space point → level point, following the host if it has moved since.</summary>
    public Point ToLevel(Vector3 space)
    {
        var w = space + HostOffset(Host);
        return new Point(w.X, -w.Y);
    }

    /// <summary>The preview is always placed without a host.</summary>
    public static Point PreviewToLevel(Vector3 space) => new(space.X, -space.Y);

    // ================= PLACEMENT ================= //

    private async Task<bool> PlaceAtAsync(Vector3 space, SceneElement? host)
    {
        var model = await EnsureModelAsync();
        if (model == null) return false;
        var bin = await ResolvePlacementBinAsync();
        if (bin == null) return false;
        return Place(space, host, model, bin, PivotNodeName, StayInPlace, Animation.StartFrame);
    }

    private bool Place(Vector3 space, SceneElement? host, MoveModel model, byte[] bin, string pivot, bool stayInPlace, int startFrame)
    {
        try
        {
            Animation.Reverse = Reverse;
            Animation.StartFrame = startFrame;
            Animation.PlaceAt(space, model, bin, pivot, stayInPlace, Now);
            Host = host;
            _lastPlacedPosition = space;
            IsPlaced = true;
            StatusMessage = string.Empty;
            SyncFrames();
            UpdateTimer();
            Follow();
            Changed?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            Report(ex.Message);
            return false;
        }
    }

    private async Task<byte[]?> ResolvePlacementBinAsync()
    {
        switch (PlacementMode)
        {
            case MovePlacementMode.Custom:
            {
                if (string.IsNullOrWhiteSpace(CustomBinPath))
                {
                    Report("Custom Bin Path is empty.");
                    return null;
                }
                if (_customBinData != null && CustomBinPath == _customBinName)
                    return _customBinData;
                return await ReadAnyAsync(CustomBinPath.Trim());
            }
            case MovePlacementMode.Tricks:
            {
                if (!TricksLoaded) await DiscoverTricksAsync();
                if (SelectedTrick == null)
                {
                    Report("No trick selected.");
                    return null;
                }
                string? path = await ResolveProjectFileAsync($"{BinsRelativeFolder}/{SelectedTrick.FileName}");
                if (path == null)
                {
                    Report($"Animation bin not found: {VPath.Combine(_editor.Project.Path, BinsRelativeFolder, SelectedTrick.FileName)}");
                    return null;
                }
                return await AppServices.FileSystem.ReadAllBytesAsync(path);
            }
            default:
                return await ReadAnyAsync(VPath.Combine(MovementFolder, MovementBins[MovementType]));
        }
    }

    /// <summary>A path inside the Vectorier folder, or (desktop) an absolute path.</summary>
    private async Task<byte[]?> ReadAnyAsync(string path)
    {
        try
        {
            var fs = AppServices.FileSystem;
            if (!Path.IsPathRooted(path) && await fs.FileExistsAsync(path))
                return await fs.ReadAllBytesAsync(path);
            if (Path.IsPathRooted(path) && !OperatingSystem.IsBrowser() && File.Exists(path))
                return await File.ReadAllBytesAsync(path);
        }
        catch (Exception ex)
        {
            Report(ex.Message);
            return null;
        }
        Report("Bin file not found: " + path);
        return null;
    }

    /// <summary>Unity ActiveProject.ResolveFile: the active project, then the Vector project.</summary>
    private async Task<string?> ResolveProjectFileAsync(string relative)
    {
        var fs = AppServices.FileSystem;
        string path = VPath.Combine(_editor.Project.Path, relative);
        if (await fs.FileExistsAsync(path)) return path;
        string fallback = VPath.Combine("Projects", DefaultProject, relative);
        return await fs.FileExistsAsync(fallback) ? fallback : null;
    }

    private async Task<XElement?> LoadMovesRootAsync()
    {
        string? path = await ResolveProjectFileAsync(MovesRelativePath);
        if (path == null)
            throw new FileNotFoundException("moves.xml not found: " + VPath.Combine(_editor.Project.Path, MovesRelativePath));
        var doc = XmlHelpers.ParseDocument(await AppServices.FileSystem.ReadAllBytesAsync(path));
        return doc.Root ?? throw new InvalidDataException("moves.xml has no root element.");
    }

    private static XElement? FindMove(XElement root, string name) =>
        root.Element("Moves")?.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));

    private static XElement? TrickGroup(XElement root) =>
        root.Element("ReactionGroups")?.Elements("ReactionGroup")
            .FirstOrDefault(g => string.Equals((string?)g.Attribute("Name"), "TrickGroup", StringComparison.OrdinalIgnoreCase));

    // ---------------- Spawn ----------------

    /// <summary>Spawn "Preview Animation": plays the spawn's "Move|Frame" at the spawn point.</summary>
    public async Task PreviewSpawnAsync(SpawnElement spawn)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(spawn.Animation))
                throw new InvalidDataException("Animation string is empty.");
            string[] parts = spawn.Animation.Split('|');
            string animationName = parts[0];
            int startFrame = 0;
            if (parts.Length > 1) int.TryParse(parts[1], out startFrame);

            string pivot = "NPivot";
            byte[]? bin;
            // JumpOff and Run come from the editor's own animations folder (Unity overrides).
            if (animationName.Equals("JumpOff", StringComparison.OrdinalIgnoreCase))
            {
                bin = await ReadAnyAsync(VPath.Combine(MovementFolder, "jump_off_fly.bin"));
                startFrame = 0;
            }
            else if (animationName.Equals("Run", StringComparison.OrdinalIgnoreCase))
            {
                bin = await ReadAnyAsync(VPath.Combine(MovementFolder, "run.bin"));
                startFrame = 0;
            }
            else
            {
                var root = await LoadMovesRootAsync();
                var move = FindMove(root!, animationName) ?? throw new InvalidDataException($"Could not find <Moves><{animationName} ... /> in moves.xml.");
                string? fileName = (string?)move.Attribute("FileName");
                if (string.IsNullOrWhiteSpace(fileName))
                    throw new InvalidDataException($"Move '{animationName}' does not have a FileName attribute.");
                if ((string?)move.Attribute("PivotNode") is { Length: > 0 } p && !string.IsNullOrWhiteSpace(p))
                    pivot = p;
                string? path = await ResolveProjectFileAsync($"{BinsRelativeFolder}/{fileName}")
                               ?? throw new FileNotFoundException("Animation bin not found: " + VPath.Combine(_editor.Project.Path, BinsRelativeFolder, fileName));
                bin = await AppServices.FileSystem.ReadAllBytesAsync(path);
            }
            if (bin == null) return;

            var model = await EnsureModelAsync();
            if (model == null) return;
            StopPlacement();
            var host = (SceneElement?)spawn.Parent ?? spawn;
            if (Place(ToSpace(spawn.WorldPosition, host), host, model, bin, pivot, false, startFrame))
            {
                PivotNodeName = pivot;
                StayInPlace = false;
            }
        }
        catch (Exception ex)
        {
            Report($"Preview Animation failed for '{spawn.Name}': {ex.Message}");
        }
    }

    // ---------------- Trick Area ----------------

    /// <summary>Trick Area "Preview Animation": the TrickGroup reaction named like the area (or its parent).</summary>
    public Task PreviewAreaAsync(AreaElement area) => PreviewAreaAsync(TargetFor(area));

    public async Task PreviewAreaAsync(AreaTarget target)
    {
        var area = target.Area;
        if (area.AreaType != "Trick")
        {
            Report("Preview Animation is only available when the Area's Type is Trick.");
            return;
        }
        try
        {
            var root = await LoadMovesRootAsync();
            var group = TrickGroup(root!) ?? throw new InvalidDataException("Could not find <ReactionGroup Name=\"TrickGroup\"> in moves.xml.");
            var candidates = new[] { area.Name, target.ParentName }
                .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).Cast<string>().ToArray();
            bool Matches(XElement e, string attr) => (string?)e.Attribute(attr) is { } v && !string.IsNullOrWhiteSpace(v) &&
                                                      candidates.Any(c => string.Equals(v, c, StringComparison.OrdinalIgnoreCase));
            var reaction = group.Elements().FirstOrDefault(e => Matches(e, "TriggerName") || Matches(e, "AreaName"))
                           ?? throw new InvalidDataException($"Could not find a TrickGroup entry with TriggerName or AreaName matching: {string.Join(", ", candidates)}");
            string moveName = reaction.Name.LocalName;
            var move = FindMove(root!, moveName) ?? throw new InvalidDataException($"Could not find <Moves><{moveName} ... /> in moves.xml.");
            string? fileName = (string?)move.Attribute("FileName");
            if (string.IsNullOrWhiteSpace(fileName))
                throw new InvalidDataException($"Move '{moveName}' does not have a FileName attribute.");
            string pivot = (string?)move.Attribute("PivotNode") is { } p && !string.IsNullOrWhiteSpace(p) ? p : "NPivot";
            int firstFrame = 0;
            if ((string?)reaction.Attribute("FirstFrame") is { } ff && !string.IsNullOrWhiteSpace(ff))
                int.TryParse(ff, out firstFrame);
            string? path = await ResolveProjectFileAsync($"{BinsRelativeFolder}/{fileName}")
                           ?? throw new FileNotFoundException("Animation bin not found: " + VPath.Combine(_editor.Project.Path, BinsRelativeFolder, fileName));
            byte[] bin = await AppServices.FileSystem.ReadAllBytesAsync(path);

            var model = await EnsureModelAsync();
            if (model == null) return;
            Animation.Reverse = Reverse;
            Animation.EnsureModelLoadedForPreview(model);

            // Unity GetAreaPreviewPosePlacementPosition: the COM sits 20 units past the area's far side,
            // the pivot on the area's bottom.
            if (!model.TryGetNodeIndex(pivot, out int pivotIndex))
                throw new InvalidDataException($"PivotNode '{pivot}' was not found in the model XML.");
            if (!model.TryGetNodeIndex("COM", out int comIndex))
                throw new InvalidDataException("Node 'COM' was not found in the model XML.");
            var pose = Animation.PreviewPose!;
            var o = target.Origin;
            var bounds = area.GetLocalBounds().Translate(new Avalonia.Vector(o.X, o.Y));
            const double finalXOffset = 20;
            double comToPivotX = pose[comIndex].X - pose[pivotIndex].X;
            double targetX = Reverse ? bounds.Left - finalXOffset - comToPivotX : bounds.Right + finalXOffset - comToPivotX;
            double targetY = bounds.Bottom; // Unity bounds.min.y (Y up) = the bottom edge

            StopPlacement();
            var host = target.Host;
            if (Place(ToSpace(new Point(targetX, targetY), host), host, model, bin, pivot, false, firstFrame))
            {
                PivotNodeName = pivot;
                StayInPlace = false;
            }
        }
        catch (Exception ex)
        {
            Report($"Preview Animation failed for '{area.Name}': {ex.Message}");
        }
    }

    // ---------------- Tricks ----------------

    /// <summary>The TrickGroup reactions of moves.xml whose bin exists in animations/data.</summary>
    private async Task DiscoverTricksAsync()
    {
        string? previous = SelectedTrick?.Identifier;
        Tricks.Clear();
        try
        {
            var root = await LoadMovesRootAsync();
            var group = TrickGroup(root!);
            var moves = root!.Element("Moves");
            if (group != null && moves != null)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var reaction in group.Elements())
                {
                    var move = FindMove(root, reaction.Name.LocalName);
                    string? fileName = (string?)move?.Attribute("FileName");
                    if (string.IsNullOrWhiteSpace(fileName)) continue;
                    if (await ResolveProjectFileAsync($"{BinsRelativeFolder}/{fileName}") == null) continue;
                    string id = fileName.Equals("360_wall_hop.bin", StringComparison.OrdinalIgnoreCase)
                        ? "Wall_Hop_360"
                        : Path.GetFileNameWithoutExtension(fileName);
                    if (!seen.Add(id))
                    {
                        // Later entries replace earlier ones (Unity dictionary assignment).
                        var old = Tricks.First(t => string.Equals(t.Identifier, id, StringComparison.OrdinalIgnoreCase));
                        Tricks[Tricks.IndexOf(old)] = new TrickEntry(id, fileName);
                        continue;
                    }
                    Tricks.Add(new TrickEntry(id, fileName));
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[Move Visualizer] tricks: " + ex.Message);
        }
        TricksLoaded = true;
        OnPropertyChanged(nameof(HasTricks));
        SelectedTrick = Tricks.FirstOrDefault(t => string.Equals(t.Identifier, previous, StringComparison.OrdinalIgnoreCase)) ?? Tricks.FirstOrDefault();
        _ = LoadTrickIconsAsync(Tricks.ToList());
    }

    private async Task LoadTrickIconsAsync(List<TrickEntry> tricks)
    {
        foreach (var trick in tricks)
        {
            try
            {
                string? path = await ResolveProjectFileAsync($"{TrickIconsRelativeFolder}/{trick.ImageName}.png");
                if (path != null)
                    trick.Icon = await ImageLoader.LoadAsync(AppServices.FileSystem, path, 128);
            }
            catch
            {
                // Missing or broken images show "No Image".
            }
        }
    }

    // ================= MODEL ================= //

    private Task<MoveModel?> EnsureModelAsync()
    {
        if (_model != null) return Task.FromResult<MoveModel?>(_model);
        return _modelLoad ??= LoadModelAsync();
    }

    private async Task<MoveModel?> LoadModelAsync()
    {
        try
        {
            var fs = AppServices.FileSystem;
            if (!await fs.FileExistsAsync(ModelXmlPath))
                throw new FileNotFoundException("Model XML not found: " + ModelXmlPath);
            byte[] data = await fs.ReadAllBytesAsync(ModelXmlPath);
            _model = await Task.Run(() => MoveModel.Parse(data));
            Animation.Reverse = Reverse;
            Animation.EnsureModelLoadedForPreview(_model);
            return _model;
        }
        catch (Exception ex)
        {
            _modelLoad = null;
            Report(ex.Message);
            return null;
        }
    }

    // ================= PLAYBACK ================= //

    private void UpdateTimer()
    {
        bool run = IsPlaced && Animation.IsOffsetInitialized && Animation.IsPlaying;
        if (run && !_timer.IsEnabled)
        {
            _timer.Start();
        }
        else if (!run && _timer.IsEnabled)
        {
            _timer.Stop();
        }
    }

    private void UpdatePlayback()
    {
        if (!Animation.TryAdvancePlayback(Now))
            return;
        _syncingFrame = true;
        Frame = Animation.CurrentFrameIndex;
        _syncingFrame = false;
        Follow();
        Changed?.Invoke();
    }

    private void AfterFrameApplied()
    {
        _syncingFrame = true;
        Frame = Animation.CurrentFrameIndex;
        _syncingFrame = false;
        Follow();
        Changed?.Invoke();
    }

    /// <summary>Follow Camera: keeps the Scene centered on the model's Camera node.</summary>
    private void Follow()
    {
        if (!FollowCamera || !IsPlaced) return;
        if (Animation.GetAnimationNode("Camera") is { } camera)
            FollowRequested?.Invoke(ToLevel(camera));
    }

    private void ResetAll()
    {
        IsPlaced = false;
        Host = null;
        Animation.ResetAll();
        Animation.Reverse = Reverse;
        if (_model != null)
            Animation.EnsureModelLoadedForPreview(_model);
        SyncFrames();
        UpdateTimer();
    }

    /// <summary>Another level was opened: the character belonged to the previous one.</summary>
    public void OnDocumentReplaced()
    {
        StopPlacement();
        if (IsPlaced) Clear();
        RefreshSelectedArea();
    }

    /// <summary>Called when the editor closes.</summary>
    public void Stop()
    {
        _timer.Stop();
        IsPlacementActive = false;
        _editor.SelectionChanged -= RefreshSelectedArea;
    }

    private void Report(string message)
    {
        StatusMessage = message;
        _editor.StatusText = "Move Visualizer: " + message;
    }
}
