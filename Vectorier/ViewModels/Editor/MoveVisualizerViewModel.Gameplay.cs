using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Vectorier.Models.Scene;
using Vectorier.Services;
using Vectorier.Services.Assets;
using Vectorier.Services.Localization;
using Vectorier.Services.MoveVisualizer;

namespace Vectorier.ViewModels.Editor;

public partial class MoveVisualizerViewModel
{
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
