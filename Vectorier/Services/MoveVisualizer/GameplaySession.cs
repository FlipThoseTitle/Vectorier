using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Xml.Linq;
using Vectorier.Gameplay;
using Vectorier.Gameplay.Animation;
using Vectorier.Gameplay.Camera;
using Vectorier.Gameplay.Controllers;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.User;
using Vectorier.Models.Scene;
using Vectorier.Services.Game;
using Vectorier.Services.Projects;
using Vectorier.Services.Xml;

namespace Vectorier.Services.MoveVisualizer;

/// <summary>
/// What a gameplay spawn starts with: where, the node of the model's rest pose put there (the game uses NPivot) and
/// its first move ("JumpOff|18", "JumpOff|18|1" reversed).
/// </summary>
public sealed record GameplaySpawn(System.Numerics.Vector2 Position, string Animation, string PivotNode = "NPivot");

/// <summary>
/// Move Visualizer, Gameplay spawn mode: the editor's integrated game (Gameplay folder, <see cref="GameHost"/>) runs
/// a level made of the open level's Platforms, Trapezoids and Areas only, with Editor/MoveVisualizer/runner.xml as
/// the player. The game moves the character (its moves, physics, platforms and Areas); nothing is drawn by it here:
/// the Move Visualizer draws the character on the Scene View. It advances in the game's fixed 1/60 s ticks and is
/// controlled with the arrow keys. A level played in the Game View takes the game back (<see cref="GameHost.Taken"/>).
/// </summary>
public sealed class GameplaySession
{
    public const double TickSeconds = 1.0 / 60.0;

    /// <summary>The game's spawn move.</summary>
    public const string SpawnMove = "JumpOff";
    public const int SpawnFrame = 18;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastTime;
    private double _accumulator;

    private LevelMainController? _controller;
    private ModelHuman? _model;

    // The game is one: a level played takes it, this character is gone then.
    private void OnTaken()
    {
        GameHost.Current.Taken -= OnTaken;
        if (_controller == null)
            return;
        _controller = null;
        _model = null;
        Taken?.Invoke();
    }

    /// <summary>The game was taken by a level played in the Game View (the character is gone).</summary>
    public event Action? Taken;

    /// <summary>The last spawn (Restart and Reverse spawn there again).</summary>
    public GameplaySpawn? LastSpawn { get; private set; }

    /// <summary>The character is in the game (and the game is still this session's).</summary>
    public bool IsActive => _model != null && _controller != null && LevelMainController.current == _controller;

    public bool IsDead => IsActive && _model!.IsDeath;

    /// <summary>The move playing (null without a character).</summary>
    public string? MoveName => IsActive && _model!.IsPlay ? _model.AnimationName : null;

    /// <summary>The playing move ("Run", "JumpOff"...) and its frame, or the physics after a death.</summary>
    public string StateText => !IsActive ? string.Empty
        : _model!.IsDeath ? "Death (physics)"
        : _model.IsPlay ? string.Format(CultureInfo.InvariantCulture, "{0} · {1}", _model.AnimationName, _model.CurrentFrame)
        : _model.IsPhysics ? "Physics" : string.Empty;

    // ================= LOADING ================= //

    private static readonly System.Threading.SemaphoreSlim LoadLock = new(1, 1);

    /// <summary>True while the moves are being parsed: a runner must not tick then.</summary>
    public static bool IsLoading { get; private set; }

    /// <summary>
    /// Readies the game: the project's moves.xml and move .bins (parsed again only when they changed) and runner.xml.
    /// A level being played in the Game View stops.
    /// </summary>
    public static async Task LoadAsync(ProjectInfo project, byte[] runner)
    {
        await LoadLock.WaitAsync();
        try
        {
            IsLoading = true;
            await GameHost.Current.PrepareMoveVisualizerAsync(project, runner);
            if (!Animations.Animation.ContainsKey(SpawnMove))
                throw new System.IO.InvalidDataException(Localization.Loc.F("moves.xml has no '{0}' move (the spawn move).", SpawnMove));
        }
        finally
        {
            IsLoading = false;
            LoadLock.Release();
        }
    }

    // ================= THE LEVEL ================= //

    /// <summary>
    /// The level the game plays: the open level's Platforms, Trapezoids and Areas (Catch and Help Areas, which belong
    /// to the hunters and the tutorial, are left out), each as the editor saves it, at its level position (Objects'
    /// own and referenced content included; Hunter Mode-only elements left out). Everything else (images,
    /// triggers, items, models) is not part of it: only the character moves.
    /// </summary>
    private static XDocument BuildLevel(Func<ObjectDefinition, IEnumerable<SceneElement>> content, IEnumerable<SceneElement> roots)
    {
        var writer = new SceneXmlWriter();
        var elements = new XElement("Content");

        void Visit(SceneElement element, Vector2 parent, int depth)
        {
            if (depth > 48 || element.Selection == ModeSelection.HunterMode)
                return;
            switch (element)
            {
                case ObjectElement obj:
                {
                    var inner = parent + new Vector2((float)obj.X, (float)obj.Y);
                    if (obj.Reference is { } def)
                    {
                        foreach (var child in content(def))
                            Visit(child, inner, depth + 1);
                    }
                    foreach (var child in obj.Children)
                        Visit(child, inner, depth + 1);
                    return;
                }
                case PlatformElement or TrapezoidElement or AreaElement:
                {
                    if (writer.WriteElement(element) is not XElement x)
                        return;
                    if (x.Name.LocalName == "Area" && (string?)x.Attribute("Type") is "Catch" or "Help")
                        return;
                    // The game puts an element at its object's position plus its own: one object per position.
                    elements.Add(new XElement("Object",
                        new XAttribute("X", parent.X.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("Y", parent.Y.ToString(CultureInfo.InvariantCulture)),
                        new XElement("Content", x)));
                    return;
                }
            }
        }

        foreach (var root in roots)
            Visit(root, Vector2.Zero, 0);

        // The game's level file: no sets, models or music; the track's one layer is the player's (factor 1).
        return new XDocument(new XElement("Root",
            new XElement("Coins", new XAttribute("Value", "0")),
            new XElement("Music", new XAttribute("Name", string.Empty)),
            new XElement("Track",
                new XElement("Object", new XAttribute("Factor", "1"), new XAttribute("X", "0"), new XAttribute("Y", "0"), elements))));
    }

    // ================= SPAWNING ================= //

    /// <summary>
    /// Spawns the character (or spawns it again: there is only ever one) with the spawn's move, on the level as it
    /// is now. A click puts the picked pivot node of the move's first frame on the cursor; a Spawn element puts the
    /// model's NPivot on it, as the game does.
    /// </summary>
    public void Spawn(GameplaySpawn spawn, Func<ObjectDefinition, IEnumerable<SceneElement>> content, IEnumerable<SceneElement> roots)
    {
        Clear();
        LastSpawn = spawn;
        var level = BuildLevel(content, roots);
        GameFiles.Set($"xmlroot/levels/{GameHost.MoveVisualizerLevel}.xml",
            System.Text.Encoding.UTF8.GetBytes(level.ToString(SaveOptions.DisableFormatting)));

        using (GameHost.Invariant())
        {
            GameState.ResetForNewGame(false);
            LevelMainController.Init(GameHost.MoveVisualizerLevel, false);
            var controller = LevelMainController.current;

            // The game's player: the self model, allowed tricks, never removed after a death (it stays in physics).
            // Its model is runner.xml alone (the game's 0.xml and skins in one file).
            var user = new UserData("")
            {
                Name = "Player",
                BirthSpawn = string.Empty,
                IsSelf = true,
                IsTrick = true,
                Color = Gameplay.Rl.Color.Black,
                Skins = new List<string>(),
                LiveTime = 1e7f,
            };
            user.Init();
            user.Skins = new List<string> { GameHost.MoveVisualizerModel };
            var model = new ModelHuman(user);
            controller.Location.Models.Add(model);
            model.Reset();

            // ModelHuman.Play(SpawnRunner): the rest pose's NPivot (here: the picked pivot node) on the spawn point,
            // then the spawn's move, which starts from that pose.
            var parts = spawn.Animation.Split('|');
            var reaction = AnimationLoader.ParseReaction(parts.Length >= 2 ? parts : new[] { parts[0], "0" });
            string pivot = model.GetNode(spawn.PivotNode) != null ? spawn.PivotNode : "NPivot";
            model.ControllerPhysics.NodeReset();
            model.Position(new Vector3(spawn.Position, 0), pivot);
            model.PlayAnimation(reaction);
            model.IsEnabled = true;

            // The camera follows the model's Camera node (in a level, a start trigger hands it over).
            var camera = controller.Camera;
            camera.StartPosition(model.ModelObject.CameraNode.Start);
            camera.Node = model.ModelObject.CameraNode;

            _controller = controller;
            _model = model;
        }
        GameHost.Current.Taken += OnTaken;

        _lastTime = _clock.Elapsed.TotalSeconds;
        _accumulator = 0;
    }

    public void Clear()
    {
        GameHost.Current.Taken -= OnTaken;
        if (IsActive)
        {
            using (GameHost.Invariant())
                GameState.ResetForNewGame(false);
        }
        _model = null;
        _controller = null;
        LastSpawn = null;
    }

    // ================= RUNNING ================= //

    /// <summary>
    /// Runs the ticks due since the last call (the game's fixed 1/60 s steps; a long pause does not fast-forward
    /// more than a quarter of a second). True when the character moved.
    /// </summary>
    public bool Advance()
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Min(now - _lastTime, 0.25);
        _lastTime = now;
        if (!IsActive)
            return false;
        _accumulator += dt;
        bool moved = false;
        while (_accumulator >= TickSeconds)
        {
            _accumulator -= TickSeconds;
            Tick();
            moved = true;
        }
        return moved;
    }

    /// <summary>One game tick (LevelMainController.Render: the camera, then the models and the level's runners).</summary>
    public void Tick()
    {
        if (!IsActive)
            return;
        using (GameHost.Invariant())
            _controller!.Render();
    }

    /// <summary>An arrow key was pressed (the game reacts to presses, not to keys held down).</summary>
    public void Press(Gameplay.Rl.KeyboardKey key)
    {
        if (!IsActive || _model!.IsDeath)
            return;
        using (GameHost.Invariant())
            _model.ControllerKeys.SetKeyVariable(new KeyVariables(key));
    }

    // ================= DRAWING ================= //

    /// <summary>The character (runner.xml as the game holds it, level coordinates), drawn by the game's ModelRender.</summary>
    public ModelObject? Character => IsActive ? _model!.ModelObject : null;

    /// <summary>Where the game's camera looks (it follows the model's Camera node smoothly), in level coordinates.</summary>
    public Avalonia.Point? CameraPosition => IsActive ? new Avalonia.Point(_controller!.Camera.X, _controller.Camera.Y) : null;

    /// <summary>The model's Camera node, in level coordinates.</summary>
    public Avalonia.Point? CameraNode => IsActive && _model!.ModelObject.CameraNode?.Node is { } node ? new Avalonia.Point(node.Start.X, node.Start.Y) : null;
}
