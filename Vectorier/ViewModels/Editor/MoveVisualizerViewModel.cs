using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Models.Scene;
using Vectorier.Services.Localization;
using Vectorier.Services.MoveVisualizer;

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
}
