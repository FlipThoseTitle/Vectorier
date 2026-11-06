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
using Vectorier.Services.Localization;

namespace Vectorier.ViewModels.Editor;

/// <summary>
/// Animation: plays one of the editor's movement bins where you click. Gameplay: spawns a character you control with
/// the arrow keys (the game's movement, see <see cref="GameplaySession"/>).
/// </summary>
public enum MoveSpawnMode { Animation, Gameplay }

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

/// <summary>Where the placement preview snapped to (drawn as a line and a dot in the Scene).</summary>
public readonly record struct MoveSnapIndicator(Point Cursor, Point Target, bool IsCorner);

/// <summary>
/// Move Visualizer (port of Unity's EditorScripts/Tools/MoveVisualizer.cs): places the runner model in the
/// Scene and plays a move on it, to check jump distances and trick timing while building the level (Animation spawn
/// mode), or spawns a character that runs on the level with the game's movement and is controlled with the arrow
/// keys (Gameplay spawn mode). The character is drawn as a black silhouette and stays after spawning stops.
/// </summary>
public sealed partial class MoveVisualizerViewModel : ObservableObject
{
    public const string ModelXmlPath = "Editor/MoveVisualizer/runner.xml";
    public const string MovementFolder = "Editor/MoveVisualizer/animations";
    private const string MovesRelativePath = "animations/moves.xml";
    private const string BinsRelativeFolder = "animations/data";
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

    public static IReadOnlyList<MoveSpawnMode> SpawnModes { get; } = Enum.GetValues<MoveSpawnMode>();
    public static IReadOnlyList<MovementType> MovementTypes { get; } = Enum.GetValues<MovementType>();
    public static IReadOnlyList<string> PivotNodePresets { get; } = new[] { "NPivot", "DetectorH", "DetectorV" };

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static double Now => Clock.Elapsed.TotalSeconds;

    private readonly EditorViewModel _editor;
    private readonly DispatcherTimer _timer;
    private MoveModel? _model;
    private Task<MoveModel?>? _modelLoad;
    private Vector3 _lastPlacedPosition; // animation space
    private bool _syncingFrame;

    public MoveVisualizerViewModel(EditorViewModel editor)
    {
        _editor = editor;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1.0 / MoveAnimation.TargetFps), DispatcherPriority.Render, (_, _) => UpdatePlayback());
        _gameplayTimer = new DispatcherTimer(TimeSpan.FromSeconds(GameplaySession.TickSeconds / 2), DispatcherPriority.Render, (_, _) => UpdateGameplay());
        editor.SelectionChanged += RefreshSelectedArea;
        // A level played in the Game View takes the game: the gameplay character is gone.
        Gameplay.Taken += () =>
        {
            if (!IsGameplaySpawned)
                return;
            ClearGameplay();
            Changed?.Invoke();
        };
    }

    public MoveAnimation Animation { get; } = new();

    /// <summary>Element the placed animation follows (Unity's placement host transform): the Spawn / Area's parent.</summary>
    public SceneElement? Host { get; private set; }

    /// <summary>Raised when the Scene must redraw the character.</summary>
    public event Action? Changed;

    /// <summary>Follow Camera: the Scene centers on this level point.</summary>
    public event Action<Point>? FollowRequested;

    // ================= SETTINGS ================= //

    [ObservableProperty] private bool _renderTrajectory = true;
    [ObservableProperty] private bool _followCamera;
    [ObservableProperty] private bool _reverse;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnimationMode), nameof(IsGameplayMode), nameof(MainButtonText), nameof(ShowsSelectedTrickArea))]
    private MoveSpawnMode _spawnMode = MoveSpawnMode.Animation;

    [ObservableProperty] private MovementType _movementType = MovementType.Jump;
    [ObservableProperty] private string _pivotNodeName = "DetectorH";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MainButtonText))]
    private bool _isPlacementActive;

    [ObservableProperty] private bool _isPlaced;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public bool IsAnimationMode => SpawnMode == MoveSpawnMode.Animation;
    public bool IsGameplayMode => SpawnMode == MoveSpawnMode.Gameplay;

    /// <summary>Hold C while placing: the pivot sticks to the nearest platform corner or edge.</summary>
    public bool PlatformSnapHeld { get; set; }

    public MoveSnapIndicator? SnapIndicator { get; private set; }

    // ================= SELECTED TRICK AREA ================= //

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTrickArea), nameof(ShowsSelectedTrickArea), nameof(MainButtonText), nameof(SelectedAreaName))]
    private AreaElement? _selectedArea;

    public bool HasSelectedTrickArea => SelectedArea is { AreaType: "Trick" };

    /// <summary>Animation mode with a Trick Area selected: the main button previews its trick.</summary>
    public bool ShowsSelectedTrickArea => HasSelectedTrickArea && IsAnimationMode;

    public string SelectedAreaName => SelectedArea?.Name ?? string.Empty;

    public string MainButtonText => ShowsSelectedTrickArea ? Loc.T("Preview Animation") : IsPlacementActive ? Loc.T("Stop") : Loc.T("Spawn");

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
        {
            OnPropertyChanged(nameof(HasSelectedTrickArea));
            OnPropertyChanged(nameof(ShowsSelectedTrickArea));
        }
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

    public string PlayButtonText => IsPlaying ? Loc.T("Pause") : Loc.T("Play");

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

    partial void OnRenderTrajectoryChanged(bool value) => Changed?.Invoke();
    partial void OnFollowCameraChanged(bool value) => Follow();

    /// <summary>One character at a time: changing the mode stops spawning and removes the other mode's character.</summary>
    partial void OnSpawnModeChanged(MoveSpawnMode value)
    {
        StopPlacement();
        if (value == MoveSpawnMode.Gameplay)
        {
            if (IsPlaced)
                ResetAll();
        }
        else
        {
            ClearGameplay();
        }
        StatusMessage = string.Empty;
        Changed?.Invoke();
    }

    async partial void OnReverseChanged(bool value)
    {
        // Gameplay: the character spawns again where it last spawned, with the move reversed (|1) or not.
        if (IsGameplayMode)
        {
            if (_suppressReverseRespawn)
                return;
            if (_autoTrick is { } trick)
                await PreviewAreaGameplayAsync(trick.Target); // runs in from the Area's other side
            else if (Gameplay.LastSpawn is { } last)
                await SpawnGameplayAsync(last with { Animation = WithReverse(last.Animation, value) });
            return;
        }
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
        if (ShowsSelectedTrickArea && _selectedTarget != null)
        {
            await PreviewAreaAsync(_selectedTarget);
            return;
        }
        IsPlacementActive = !IsPlacementActive;
        PlatformSnapHeld = false;
        SnapIndicator = null;
        if (IsPlacementActive)
        {
            if (await EnsureModelAsync() == null || (IsGameplayMode && !await EnsureGameplayAsync()))
                IsPlacementActive = false;
            else
                StatusMessage = IsGameplayMode
                    ? Loc.T("Click in the Scene to spawn the runner, then play it with the arrow keys. Hold C to stick to platforms.")
                    : Loc.T("Click in the Scene to place the runner. Hold C to stick to platforms.");
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
        GameplayPreviewNodes = null;
        StatusMessage = string.Empty;
        Changed?.Invoke();
    }

    [RelayCommand]
    private void Clear()
    {
        ResetAll();
        ClearGameplay();
        StatusMessage = string.Empty;
        Changed?.Invoke();
    }

    [RelayCommand]
    private void PlayPause()
    {
        IsPlaying = !IsPlaying;
    }

    [RelayCommand]
    private async Task Restart()
    {
        if (IsGameplayMode)
        {
            if (_autoTrick is { } trick)
                await PreviewAreaGameplayAsync(trick.Target);
            else if (Gameplay.LastSpawn is { } last)
                await SpawnGameplayAsync(last with { Animation = WithReverse(last.Animation, Reverse) });
            return;
        }
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
        if (IsGameplayMode)
        {
            UpdateGameplayPreview(target);
            Changed?.Invoke();
            return;
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
        if (IsGameplayMode)
        {
            // The spawn move is always JumpOff from frame 18 (|1: reversed), as the game spawns the player.
            string animation = WithReverse($"{GameplaySession.SpawnMove}|{GameplaySession.SpawnFrame}", Reverse);
            if (await SpawnGameplayAsync(new GameplaySpawn(new System.Numerics.Vector2((float)target.X, (float)target.Y), animation, PivotNodeName)))
            {
                GameplayPreviewNodes = null;
                Changed?.Invoke();
            }
            return;
        }
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
        return Place(space, host, model, bin, PivotNodeName, Animation.StartFrame);
    }

    private bool Place(Vector3 space, SceneElement? host, MoveModel model, byte[] bin, string pivot, int startFrame, bool? reverse = null)
    {
        try
        {
            Animation.Reverse = reverse ?? Reverse;
            Animation.StartFrame = startFrame;
            Animation.PlaceAt(space, model, bin, pivot, false, Now);
            Host = host;
            _lastPlacedPosition = space;
            IsPlaced = true;
            // The start pose, its cloth already hanging.
            model.Placed.ResetToRest();
            SyncPlacedModel(tick: false);
            model.Placed.Settle(60);
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

    private Task<byte[]?> ResolvePlacementBinAsync() => ReadAnyAsync(VPath.Combine(MovementFolder, MovementBins[MovementType]));

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
        Report(Loc.T("Bin file not found: ") + path);
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
            if (SpawnMode != MoveSpawnMode.Animation)
                SpawnMode = MoveSpawnMode.Animation;
            if (Place(ToSpace(spawn.WorldPosition, host), host, model, bin, pivot, startFrame, spawn.Reverse))
                PivotNodeName = pivot;
        }
        catch (Exception ex)
        {
            Report(Loc.F("Preview Animation failed for '{0}': {1}", spawn.Name, ex.Message));
        }
    }

    // ---------------- Trigger ----------------

    /// <summary>The &lt;ForceAnimation&gt; actions the trigger runs (its own loops and its template's).</summary>
    public async Task<List<Services.Triggers.TriggerForcedAnimation>> FindTriggerAnimationsAsync(TriggerElement trigger)
    {
        XElement? templates = null;
        try
        {
            string path = VPath.Combine(_editor.Project.XmlRoot, "TriggerTemplates.xml");
            if (await AppServices.FileSystem.FileExistsAsync(path))
                templates = XmlHelpers.ParseDocument(await AppServices.FileSystem.ReadAllBytesAsync(path)).Root;
        }
        catch
        {
            // without templates: only the trigger's own loops
        }
        string content = trigger.ContentXml;
        return await Task.Run(() => Services.Triggers.TriggerAnimationResolver.Find(content, templates));
    }

    /// <summary>
    /// Trigger "Preview Animation": plays the move of the trigger's Force Animation where the model meets the
    /// trigger. Model Enters the Trigger (and any other event): the COM on the trigger's left side, DetectorH on
    /// its bottom; Model Leaves the Trigger: the COM on its right side. The preview follows the trigger when it moves.
    /// </summary>
    public async Task PreviewTriggerAsync(TriggerElement trigger)
    {
        try
        {
            var animations = await FindTriggerAnimationsAsync(trigger);
            var forced = animations.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.Animation))
                         ?? throw new InvalidDataException(animations.Count == 0
                             ? "The trigger has no Force Animation."
                             : "The Force Animation's animation name could not be resolved from the trigger's variables.");

            var root = await LoadMovesRootAsync();
            var move = FindMove(root!, forced.Animation) ?? throw new InvalidDataException($"Could not find <Moves><{forced.Animation} ... /> in moves.xml.");
            string? fileName = (string?)move.Attribute("FileName");
            if (string.IsNullOrWhiteSpace(fileName))
                throw new InvalidDataException($"Move '{forced.Animation}' does not have a FileName attribute.");
            string pivot = (string?)move.Attribute("PivotNode") is { } p && !string.IsNullOrWhiteSpace(p) ? p : "NPivot";
            // Frame -1 (or none): the move's own FirstFrame, as in the game.
            int startFrame = forced.Frame;
            if (startFrame < 0)
                startFrame = int.TryParse((string?)move.Attribute("FirstFrame"), out int ff) ? ff : 0;
            string? path = await ResolveProjectFileAsync($"{BinsRelativeFolder}/{fileName}")
                           ?? throw new FileNotFoundException("Animation bin not found: " + VPath.Combine(_editor.Project.Path, BinsRelativeFolder, fileName));
            byte[] bin = await AppServices.FileSystem.ReadAllBytesAsync(path);

            var model = await EnsureModelAsync();
            if (model == null) return;
            if (!model.TryGetNodeIndex("COM", out int comIndex))
                throw new InvalidDataException("Node 'COM' was not found in the model XML.");
            if (!model.TryGetNodeIndex("DetectorH", out int detectorIndex))
                throw new InvalidDataException("Node 'DetectorH' was not found in the model XML.");

            StopPlacement();
            var host = (SceneElement)trigger;
            var bounds = trigger.GetLocalBounds().Translate(new Avalonia.Vector(trigger.ParentWorldPosition.X, trigger.ParentWorldPosition.Y));
            var target = ToSpace(new Point(forced.OnExit ? bounds.Right : bounds.Left, bounds.Bottom), host);

            // Place once, measure the start frame's COM and DetectorH, then move it so they land exactly.
            if (SpawnMode != MoveSpawnMode.Animation)
                SpawnMode = MoveSpawnMode.Animation;
            if (!Place(target, host, model, bin, pivot, startFrame, forced.Reversed) || Animation.AnimationNodes is not { } nodes)
                return;
            var shift = new Vector3(target.X - nodes[comIndex].X, target.Y - nodes[detectorIndex].Y, 0);
            if (Place(target + shift, host, model, bin, pivot, startFrame, forced.Reversed))
            {
                PivotNodeName = pivot;
                StatusMessage = Loc.F("{0} (frame {1}{2}), {3}", forced.Animation, startFrame,
                    forced.Reversed ? ", " + Loc.T("Reversed") : string.Empty,
                    forced.OnExit ? Loc.T("Model Leaves the Trigger") : Loc.T("Model Enters the Trigger"));
            }
        }
        catch (Exception ex)
        {
            Report(Loc.F("Preview Animation failed for '{0}': {1}", trigger.Name, ex.Message));
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
            Report(Loc.T("Preview Animation is only available when the Area's Type is Trick."));
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
            if (SpawnMode != MoveSpawnMode.Animation)
                SpawnMode = MoveSpawnMode.Animation;
            var host = target.Host;
            if (Place(ToSpace(new Point(targetX, targetY), host), host, model, bin, pivot, firstFrame))
                PivotNodeName = pivot;
        }
        catch (Exception ex)
        {
            Report(Loc.F("Preview Animation failed for '{0}': {1}", area.Name, ex.Message));
        }
    }

    // ================= GAMEPLAY ================= //

    private readonly DispatcherTimer _gameplayTimer;
    private Vector3[]? _gameplayRestPose;

    /// <summary>The character of the Gameplay spawn mode (the game's player movement on this level).</summary>
    public GameplaySession Gameplay { get; } = new();

    /// <summary>A gameplay character is in the level (alive or in physics after a death).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRestart))]
    private bool _isGameplaySpawned;

    /// <summary>The character's move and frame, or its death.</summary>
    [ObservableProperty] private string _gameplayStateText = string.Empty;

    public bool CanRestart => IsGameplayMode ? IsGameplaySpawned : HasFrames;

    partial void OnHasFramesChanged(bool value) => OnPropertyChanged(nameof(CanRestart));

    /// <summary>The placement preview in Gameplay mode: runner.xml's rest pose (what spawns), in level coordinates.</summary>
    public Vector3[]? GameplayPreviewNodes { get; private set; }

    /// <summary>The gameplay character (runner.xml as the game holds it, level coordinates), or null.</summary>
    public Vectorier.Gameplay.Models.ModelObject? GameplayCharacter => IsGameplaySpawned ? Gameplay.Character : null;

    /// <summary>Game View: while a gameplay character is in the level, the camera follows its Camera node (the game's camera).</summary>
    public Point? GameViewCenter => IsGameplaySpawned ? Gameplay.CameraPosition : null;

    /// <summary>"JumpOff|18" → "JumpOff|18|1" (reversed) or back.</summary>
    public static string WithReverse(string animation, bool reverse)
    {
        var parts = (animation ?? string.Empty).Split('|');
        string move = parts[0].Trim();
        string frame = parts.Length > 1 && parts[1].Trim().Length > 0 ? parts[1].Trim() : "0";
        return reverse ? $"{move}|{frame}|1" : $"{move}|{frame}";
    }

    /// <summary>Loads the project's moves, animations and runner physics (only again when they changed).</summary>
    private async Task<bool> EnsureGameplayAsync()
    {
        try
        {
            IsBusy = true;
            if (await EnsureModelAsync() is not { } model)
                return false;
            await GameplaySession.LoadAsync(_editor.Project, model.Data);
            _gameplayRestPose = model.GamePose;
            return true;
        }
        catch (Exception ex)
        {
            Report(Loc.T("Gameplay could not start: ") + ex.Message);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>The pose shown while placing in Gameplay mode (and the one spawned from), mirrored when reversed.</summary>
    /// <summary>runner.xml's rest pose in the game's space, mirrored when reversed.</summary>
    private Vector3[]? RestGamePose() => _model?.Placed.RestPose is not { } rest ? null
        : Reverse ? rest.Select(p => new Vector3(-p.X, p.Y, p.Z)).ToArray() : rest.ToArray();

    private Vector3[]? GameplayPose() => _gameplayRestPose is not { } pose ? null
        : Reverse ? pose.Select(p => new Vector3(-p.X, p.Y, p.Z)).ToArray() : pose;

    private void UpdateGameplayPreview(Point target)
    {
        if (GameplayPose() is not { } rest || _model == null || !_model.TryGetNodeIndex(PivotNodeName, out int pivot) && !_model.TryGetNodeIndex("NPivot", out pivot))
        {
            GameplayPreviewNodes = null;
            return;
        }
        var offset = new Vector3((float)target.X, (float)target.Y, 0) - rest[pivot];
        var nodes = new Vector3[rest.Length];
        for (int i = 0; i < rest.Length; i++)
            nodes[i] = rest[i] + offset;
        GameplayPreviewNodes = nodes;
    }

    /// <summary>Spawns the gameplay character (again: there is only ever one) on the level as it is now.</summary>
    private async Task<bool> SpawnGameplayAsync(GameplaySpawn spawn, AutoTrick? autoTrick = null, bool restPose = false)
    {
        _autoTrick = null;
        if (!await EnsureGameplayAsync() || _model == null)
            return false;
        // A Spawn element starts from runner.xml's own pose, as the game does (the run pose is for clicks and Areas).
        Gameplay.StartPose = restPose || spawn.RestPose ? RestGamePose() : GameplayPose();
        try
        {
            if (IsPlaced)
                ResetAll();
            Gameplay.Spawn(spawn, def =>
            {
                _editor.BuildDefinition(def);
                return def.Content;
            }, _editor.Document.Roots);
            IsGameplaySpawned = true;
            _autoTrick = autoTrick;
            GameplayStateText = Gameplay.StateText;
            StatusMessage = IsPlacementActive
                ? Loc.T("Arrow keys play the runner. Click again to spawn it somewhere else, Esc to stop spawning.")
                : Loc.T("Arrow keys play the runner.");
            if (!_gameplayTimer.IsEnabled)
                _gameplayTimer.Start();
            Gameplay.Advance(); // starts its clock
            Follow();
            Changed?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            Report(Loc.T("Gameplay could not start: ") + ex.Message);
            return false;
        }
    }

    /// <summary>The game's ticks due since the last frame, then the views follow.</summary>
    private void UpdateGameplay()
    {
        if (!IsGameplaySpawned)
        {
            _gameplayTimer.Stop();
            return;
        }
        if (GameplaySession.IsLoading)
            return; // the moves are being reloaded
        try
        {
            if (!Gameplay.Advance())
                return;
        }
        catch (Exception ex)
        {
            // A move the port does not handle: the character stops where it is.
            _gameplayTimer.Stop();
            Report(Loc.T("Gameplay stopped: ") + ex.Message);
            return;
        }
        PressTrickKey();
        string state = Gameplay.StateText;
        if (state != GameplayStateText)
            GameplayStateText = state;
        Follow();
        GameplayMoved?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>Raised after each gameplay tick (the Game View follows the character's camera).</summary>
    public event Action? GameplayMoved;

    /// <summary>An arrow key pressed while a gameplay character is in the level: true when it was used.</summary>
    public bool PressGameplayKey(Vectorier.Gameplay.Rl.KeyboardKey key)
    {
        if (!IsGameplaySpawned)
            return false;
        Gameplay.Press(key);
        return true;
    }

    private void ClearGameplay()
    {
        _gameplayTimer.Stop();
        _autoTrick = null;
        Gameplay.Clear();
        GameplayPreviewNodes = null;
        IsGameplaySpawned = false;
        GameplayStateText = string.Empty;
    }

    /// <summary>Spawn "Preview Animation" in Gameplay: the character spawns on the Spawn with its own move, as in the game.</summary>
    public async Task PreviewSpawnGameplayAsync(SpawnElement spawn)
    {
        StopPlacement();
        if (SpawnMode != MoveSpawnMode.Gameplay)
            SpawnMode = MoveSpawnMode.Gameplay;
        if (!await EnsureGameplayAsync())
            return;
        string animation = string.IsNullOrWhiteSpace(spawn.Animation) ? SpawnElement.DefaultAnimation : spawn.Animation.Trim();
        // A move moves.xml does not have: the game's spawn move instead.
        if (!Vectorier.Gameplay.Animation.Animations.Animation.ContainsKey(animation.Split('|')[0].Trim()))
            animation = WithReverse($"{GameplaySession.SpawnMove}|{GameplaySession.SpawnFrame}", spawn.Reverse);
        _suppressReverseRespawn = true;
        Reverse = spawn.Reverse;
        _suppressReverseRespawn = false;
        var p = spawn.WorldPosition;
        await SpawnGameplayAsync(new GameplaySpawn(new System.Numerics.Vector2((float)p.X, (float)p.Y), animation, RestPose: true));
    }

    /// <summary>
    /// Trick Area "Preview Animation" in Gameplay: the character spawns running, just before the Area (its COM 100
    /// before the Area's near side, its feet on the bottom edge; from the right side when Reverse is on), and the trick's key (Up) is pressed for it once it is inside, until the trick starts.
    /// </summary>
    public async Task PreviewAreaGameplayAsync(AreaTarget target)
    {
        StopPlacement();
        if (SpawnMode != MoveSpawnMode.Gameplay)
            SpawnMode = MoveSpawnMode.Gameplay;
        if (!await EnsureGameplayAsync())
            return;
        var o = target.Origin;
        var bounds = target.Area.GetLocalBounds().Translate(new Avalonia.Vector(o.X, o.Y));

        // The run (the move a trick is taken from), its first frame and the node it is placed by; the game's spawn
        // move with a longer run-up when the project has no run.
        GameplaySpawn spawn;
        if (Vectorier.Gameplay.Animation.Animations.Animation.TryGetValue(RunMove, out _))
        {
            string pivot = "NPivot";
            int firstFrame = 1;
            if (await LoadMovesRootAsync() is { } root && FindMove(root, RunMove) is { } move)
            {
                if ((string?)move.Attribute("PivotNode") is { Length: > 0 } p)
                    pivot = p;
                if (int.TryParse((string?)move.Attribute("FirstFrame"), out int f))
                    firstFrame = f;
            }
            if (_model == null || !_model.TryGetNodeIndex(pivot, out int pivotIndex))
            {
                pivot = "NPivot";
                pivotIndex = _model != null && _model.TryGetNodeIndex(pivot, out int n) ? n : -1;
            }
            // The COM of the run pose (what spawns) TrickLead before the Area's near side: the pivot goes where that puts it.
            double comX = Reverse ? bounds.Right + TrickLead : bounds.Left - TrickLead;
            double comToPivot = GameplayPose() is { } pose && pivotIndex >= 0 && _model!.TryGetNodeIndex("COM", out int comIndex)
                ? pose[pivotIndex].X - pose[comIndex].X : 0;
            double x = comX + comToPivot;
            spawn = new GameplaySpawn(new System.Numerics.Vector2((float)x, (float)bounds.Bottom),
                WithReverse($"{RunMove}|{firstFrame}", Reverse), pivot);
        }
        else
        {
            const double RunUp = 400;
            double x = Reverse ? bounds.Right + RunUp : bounds.Left - RunUp;
            spawn = new GameplaySpawn(new System.Numerics.Vector2((float)x, (float)bounds.Bottom),
                WithReverse($"{GameplaySession.SpawnMove}|{GameplaySession.SpawnFrame}", Reverse));
        }
        if (await SpawnGameplayAsync(spawn, new AutoTrick(target, bounds)))
            StatusMessage = Loc.F("The runner runs into '{0}': its trick key is pressed for it.", target.Area.Name);
    }

    /// <summary>The run a trick is taken from (moves.xml), and how far before the Area it starts.</summary>
    private const string RunMove = "RunForward";
    private const double TrickLead = 100;

    /// <summary>A Trick Area preview in Gameplay: Up is pressed inside the Area until the trick starts (3 times at most).</summary>
    private sealed class AutoTrick(AreaTarget target, Rect bounds)
    {
        public AreaTarget Target { get; } = target;
        public Rect Bounds { get; } = bounds;
        public int Presses;
        public double NextPress;
        public string? MoveAtPress;
        public bool Done;
    }

    private AutoTrick? _autoTrick;
    private readonly System.Diagnostics.Stopwatch _autoTrickClock = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>After the game's ticks: inside the Area, Up is pressed (again after a moment) until the move changes.</summary>
    private void PressTrickKey()
    {
        if (_autoTrick is not { Done: false } t || Gameplay.Character is not { } character || Gameplay.IsDead)
            return;
        string? move = Gameplay.MoveName;
        if (t.MoveAtPress != null && move != t.MoveAtPress)
        {
            t.Done = true; // the trick (or another reaction) started
            return;
        }
        if (character.GetNode("COM") is not { } com)
            return;
        double x = com.Start.X;
        if (x < t.Bounds.Left || x > t.Bounds.Right)
            return;
        double now = _autoTrickClock.Elapsed.TotalSeconds;
        if (now < t.NextPress)
            return;
        if (t.Presses >= 3)
        {
            t.Done = true;
            return;
        }
        t.Presses++;
        t.NextPress = now + 0.1;
        t.MoveAtPress = move;
        Gameplay.Press(Vectorier.Gameplay.Rl.KeyboardKey.Up);
    }

    public Task PreviewAreaGameplayAsync(AreaElement area) => PreviewAreaGameplayAsync(TargetFor(area));

    private bool _suppressReverseRespawn;

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
            var model = await Task.Run(() => MoveModel.Parse(data));
            await UseRunPoseAsync(model);
            _model = model;
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

    /// <summary>
    /// The pose shown while placing, in both Spawn Modes: RunForward at frame 2 (the pose the runner is in when it
    /// reaches a trick, so node positions such as the COM match the game's); runner.xml's rest pose when the project
    /// has no RunForward.
    /// </summary>
    private async Task UseRunPoseAsync(MoveModel model)
    {
        try
        {
            if (await LoadMovesRootAsync() is not { } root || FindMove(root, RunMove) is not { } move
                || (string?)move.Attribute("FileName") is not { Length: > 0 } fileName
                || await ResolveProjectFileAsync($"{BinsRelativeFolder}/{fileName}") is not { } path)
                return;
            byte[] bin = await AppServices.FileSystem.ReadAllBytesAsync(path);
            model.UsePose(MoveModel.ReadFrame(bin, RunPoseFrame));
        }
        catch (Exception ex)
        {
            Console.WriteLine("Move Visualizer: RunForward pose: " + ex.Message);
        }
    }

    private const int RunPoseFrame = 2;

    // ================= PLAYBACK ================= //

    private Vector3[] _placedLevel = Array.Empty<Vector3>();

    /// <summary>
    /// The placed character (runner.xml) takes the move's pose: its animated nodes in level coordinates, then (a game
    /// tick) its cloth and macro nodes. Without a tick it is only put there.
    /// </summary>
    private void SyncPlacedModel(bool tick)
    {
        if (_model == null || !IsPlaced || Animation.AnimationNodes is not { } nodes)
            return;
        int n = Math.Min(Animation.AnimatedNodeCount, nodes.Length);
        if (_placedLevel.Length != n)
            _placedLevel = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            var p = ToLevel(nodes[i]);
            _placedLevel[i] = new Vector3((float)p.X, (float)p.Y, -nodes[i].Z);
        }
        if (tick)
            _model.Placed.Tick(_placedLevel, n);
        else
            _model.Placed.Set(_placedLevel, n);
    }

    /// <summary>
    /// The placed character to draw (Animation mode), or null. Not ticking (paused, no cloth), it is put where the move
    /// is now, so it follows its host when the host is moved.
    /// </summary>
    public ModelView? PlacedCharacter
    {
        get
        {
            if (!IsPlaced || !Animation.IsOffsetInitialized || _model == null)
                return null;
            if (!_timer.IsEnabled)
                SyncPlacedModel(tick: false);
            return _model.Placed;
        }
    }

    /// <summary>The placement ghost, posed at <paramref name="nodes"/> (every node, level coordinates).</summary>
    public ModelView? GhostAt(IReadOnlyList<Vector3> nodes)
    {
        if (_model == null)
            return null;
        _model.Ghost.Set(nodes);
        return _model.Ghost;
    }

    private void UpdateTimer()
    {
        // Paused, a model with cloth still ticks (its cloth settles).
        bool run = IsPlaced && Animation.IsOffsetInitialized && (Animation.IsPlaying || _model is { Placed.HasCloth: true });
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
        {
            // Paused: the cloth still moves.
            if (IsPlaced && _model is { Placed.HasCloth: true } && !Animation.IsPlaying)
            {
                SyncPlacedModel(tick: true);
                Changed?.Invoke();
            }
            return;
        }
        SyncPlacedModel(tick: true);
        _syncingFrame = true;
        Frame = Animation.CurrentFrameIndex;
        _syncingFrame = false;
        Follow();
        Changed?.Invoke();
    }

    private void AfterFrameApplied()
    {
        SyncPlacedModel(tick: _model is { Placed.HasCloth: true });
        _syncingFrame = true;
        Frame = Animation.CurrentFrameIndex;
        _syncingFrame = false;
        Follow();
        Changed?.Invoke();
    }

    /// <summary>Follow Camera: keeps the Scene centered on the model's Camera node.</summary>
    private void Follow()
    {
        if (!FollowCamera) return;
        if (IsGameplayMode)
        {
            // The game's camera, which follows the character's Camera node.
            if (IsGameplaySpawned && Gameplay.CameraPosition is { } position)
                FollowRequested?.Invoke(position);
            return;
        }
        if (!IsPlaced) return;
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
        if (IsPlaced || IsGameplaySpawned) Clear();
        RefreshSelectedArea();
    }

    /// <summary>Called when the editor closes.</summary>
    public void Stop()
    {
        _timer.Stop();
        ClearGameplay();
        IsPlacementActive = false;
        _editor.SelectionChanged -= RefreshSelectedArea;
    }

    private void Report(string message)
    {
        StatusMessage = message;
        _editor.StatusText = Loc.T("Move Visualizer: ") + message;
    }
}
