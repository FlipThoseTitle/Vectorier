using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Xml.Linq;
using Vectorier.Gameplay;
using Vectorier.Gameplay.Animation;
using Vectorier.Gameplay.Camera;
using Vectorier.Gameplay.Controllers;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Runners;
using Vectorier.Gameplay.User;
using Vectorier.Models.Scene;
using Vectorier.Services.IO;
using Vectorier.Services.Projects;
using Vectorier.Services.Xml;
using GameLocation = Vectorier.Gameplay.Location.Location;

namespace Vectorier.Services.MoveVisualizer;

/// <summary>
/// What a gameplay spawn starts with: where, the node of the model's rest pose put there (the game uses NPivot) and
/// its first move ("JumpOff|18", "JumpOff|18|1" reversed).
/// </summary>
public sealed record GameplaySpawn(System.Numerics.Vector2 Position, string Animation, string PivotNode = "NPivot");

/// <summary>
/// Move Visualizer, Gameplay spawn mode: the game's player movement (ported from Vector-Raylib-For-Vectorier, see
/// Gameplay/README.md) running on the open level. The character stands on the level's Platforms and Trapezoids and
/// reacts to its Areas (Animation and Trick); it is controlled with the arrow keys and advances in the game's fixed
/// 1/60 s ticks. moves.xml and the animation bins come from the active project's animations folder, the runner's
/// physics (masses, edges) from its models/0.xml; the editor draws it with runner.xml.
/// </summary>
public sealed class GameplaySession
{
    public const double TickSeconds = 1.0 / 60.0;

    /// <summary>The game's spawn move.</summary>
    public const string SpawnMove = "JumpOff";
    public const int SpawnFrame = 18;

    private static string? _loadedSignature;
    private static string? _loadedProject;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastTime;
    private double _accumulator;

    private GameLocation? _location;
    private LevelMainController? _controller;
    private LocationCamera? _camera;
    private ModelHuman? _model;
    private int[]? _drawMap;

    /// <summary>The last spawn (Restart and Reverse spawn there again).</summary>
    public GameplaySpawn? LastSpawn { get; private set; }

    public bool IsActive => _model != null;

    public bool IsDead => _model?.IsDeath ?? false;

    /// <summary>The playing move ("Run", "JumpOff"...) and its frame, or the physics after a death.</summary>
    public string StateText => _model == null ? string.Empty
        : _model.IsDeath ? "Death (physics)"
        : _model.IsPlay ? string.Format(CultureInfo.InvariantCulture, "{0} · {1}", _model.AnimationName, _model.CurrentFrame)
        : _model.IsPhysics ? "Physics" : string.Empty;

    // ================= LOADING ================= //

    /// <summary>
    /// Reads moves.xml, every move's .bin and the runner's models/0.xml from the project (again when one of them
    /// changed since the last time), then parses the moves as the game does.
    /// </summary>
    public static async Task LoadAsync(ProjectInfo project)
    {
        // One load at a time: the moves are the game's shared tables, rebuilt in place.
        await LoadLock.WaitAsync();
        try
        {
            await LoadCoreAsync(project);
        }
        finally
        {
            IsLoading = false;
            LoadLock.Release();
        }
    }

    private static readonly System.Threading.SemaphoreSlim LoadLock = new(1, 1);

    /// <summary>True while the moves are being rebuilt: a runner must not tick then.</summary>
    public static bool IsLoading { get; private set; }

    private static async Task LoadCoreAsync(ProjectInfo project)
    {
        var fs = AppServices.FileSystem;
        string movesPath = VPath.Combine(project.AnimationsFolder, "moves.xml");
        if (!await fs.FileExistsAsync(movesPath))
            throw new System.IO.FileNotFoundException(Services.Localization.Loc.F("moves.xml not found: {0}", movesPath));
        byte[] moves = await fs.ReadAllBytesAsync(movesPath);

        string modelPath = VPath.Combine(project.ModelsFolder, "0.xml");
        if (!await fs.FileExistsAsync(modelPath))
            throw new System.IO.FileNotFoundException(Services.Localization.Loc.F(
                "The runner's physics model was not found: {0} (copy models/0.xml from the project template).", modelPath));

        // The move bins named in moves.xml, found whatever their casing (as the game's PathCase does).
        string dataFolder = VPath.Combine(project.AnimationsFolder, "data");
        var onDisk = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (await fs.DirectoryExistsAsync(dataFolder))
        {
            foreach (var file in await fs.GetFilesAsync(dataFolder))
                onDisk.TryAdd(VPath.GetFileName(file), file);
        }
        var bins = MoveFileNames(moves).Where(onDisk.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // Same files as last time: nothing to read again.
        var signature = new System.Text.StringBuilder();
        signature.Append(Convert.ToHexString(SHA256.HashData(moves)));
        signature.Append('|').Append(await fs.GetLastWriteTimeAsync(modelPath));
        foreach (var bin in bins)
            signature.Append('|').Append(bin).Append('@').Append(await fs.GetLastWriteTimeAsync(onDisk[bin]));
        string sig = signature.ToString();
        if (sig == _loadedSignature && project.Path == _loadedProject)
            return;

        IsLoading = true;
        GameplayFiles.Clear();
        GameplayFiles.Set("animations/moves.xml", moves);
        GameplayFiles.Set("models/0.xml", await fs.ReadAllBytesAsync(modelPath));
        foreach (var bin in bins)
            GameplayFiles.Set("animations/data/" + bin, await fs.ReadAllBytesAsync(onDisk[bin]));

        await Task.Run(() =>
        {
            AnimationLoader.Current._xmlPath = VectorPaths.Animations;
            AnimationLoader.Current.ReloadAnimations();
            foreach (var animation in Animations.ToList())
                animation.LoadBin();
        });
        if (!Animations.Animation.ContainsKey(SpawnMove))
            throw new System.IO.InvalidDataException(Services.Localization.Loc.F("moves.xml has no '{0}' move (the spawn move).", SpawnMove));
        _loadedSignature = sig;
        _loadedProject = project.Path;
    }

    private static IEnumerable<string> MoveFileNames(byte[] moves)
    {
        XElement? root;
        try
        {
            root = XmlHelpers.ParseDocument(moves).Root;
        }
        catch
        {
            yield break;
        }
        foreach (var move in root?.Element("Moves")?.Elements() ?? Enumerable.Empty<XElement>())
        {
            if ((string?)move.Attribute("FileName") is { Length: > 0 } file)
                yield return file;
        }
    }

    /// <summary>
    /// The runner's rest pose (models/0.xml, level coordinates with its NPivot at 0, 0) in the order of the drawn
    /// model: a spawn starts from it, so the placement preview shows it.
    /// </summary>
    public static System.Numerics.Vector3[]? RestPose(MoveModel drawModel)
    {
        if (!GameplayFiles.Contains("models/0.xml"))
            return null;
        var model = new ModelObject(new List<string> { "0.xml" });
        var origin = model.GetNode("NPivot")?.Start ?? Vector3.Zero;
        var pose = new Vector3[drawModel.NodeCount];
        for (int i = 0; i < pose.Length; i++)
            pose[i] = model.GetNode(drawModel.NodeNamesOrdered[i]) is { } node ? node.Start - origin : default;
        return pose;
    }

    // ================= THE LEVEL ================= //

    /// <summary>
    /// The level's Platforms, Trapezoids and Animation / Trick Areas as the game reads them from the saved level:
    /// each one from the XML the editor writes for it, at its level position (Objects' own and referenced content
    /// included; Hunter Mode-only elements left out). Catch and Help Areas and Triggers are not part of it.
    /// </summary>
    private static GameLocation BuildLocation(Func<ObjectDefinition, IEnumerable<SceneElement>> content, IEnumerable<SceneElement> roots)
    {
        var writer = new SceneXmlWriter();
        var quads = new List<QuadRunner>();
        var areas = new List<AreaRunner>();

        void Visit(SceneElement element, System.Numerics.Vector2 parent, int depth)
        {
            if (depth > 48 || element.Selection == ModeSelection.HunterMode)
                return;
            switch (element)
            {
                case ObjectElement obj:
                {
                    var inner = parent + new System.Numerics.Vector2((float)obj.X, (float)obj.Y);
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
                    var runner = CreateRunner(x);
                    if (runner == null)
                        return;
                    runner.InitRunner(parent);
                    runner.SetProperties();
                    if (runner is AreaRunner area)
                        areas.Add(area);
                    else
                        quads.Add(runner);
                    return;
                }
            }
        }

        foreach (var root in roots)
            Visit(root, System.Numerics.Vector2.Zero, 0);
        return new GameLocation(quads, areas);
    }

    /// <summary>The game's Elements.CreatePlatform / CreateTrapezoid / CreateArea, from the element's XML.</summary>
    private static QuadRunner? CreateRunner(XElement x)
    {
        float F(string name, float fallback = 0) =>
            float.TryParse((string?)x.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;
        int I(string name, int fallback = 0) =>
            int.TryParse((string?)x.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;
        bool B(string name, bool fallback) =>
            int.TryParse((string?)x.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v > 0 : fallback;
        string S(string name) => (string?)x.Attribute(name) ?? string.Empty;

        float px = F("X"), py = F("Y"), width = F("Width"), height = F("Height");
        switch (x.Name.LocalName)
        {
            case "Platform":
                return new PlatformRunner(S("Name"), px, py, width, height, B("Sticky", true));
            case "Trapezoid":
                return new TrapezoidRunner(S("Name"), I("Type", 1), px, py, width, height, F("Height1"), B("Sticky", true));
            case "Area":
                string type = S("Type");
                return type switch
                {
                    "Trick" => new TrickAreaRunner(px, py, width, height, type, S("Name"), S("ItemName"), I("Score")),
                    "Catch" or "Help" => null,
                    _ => new AreaRunner(AreaRunner.AreaType.None, px, py, width, height, type, S("Name")),
                };
        }
        return null;
    }

    // ================= SPAWNING ================= //

    /// <summary>
    /// Spawns the character (or spawns it again: there is only ever one) with the spawn's move, on the level as it
    /// is now. A click puts the picked pivot node of the move's first frame on the cursor; a Spawn element puts the
    /// model's NPivot on it, as the game does.
    /// </summary>
    public void Spawn(GameplaySpawn spawn, Func<ObjectDefinition, IEnumerable<SceneElement>> content, IEnumerable<SceneElement> roots, MoveModel drawModel)
    {
        LastSpawn = spawn;
        _location = BuildLocation(content, roots);
        _controller = new LevelMainController(_location);
        LevelMainController.current = _controller;
        TrickAreaRunner.Current = null!;

        // The game's player: the self model, allowed tricks, never removed after a death (it stays in physics).
        Model.ModelsAll.Clear();
        var user = new UserData("")
        {
            Name = "Player",
            BirthSpawn = string.Empty,
            IsSelf = true,
            IsTrick = true,
            Color = Gameplay.Color.Black,
            Skins = new List<string>(),
            LiveTime = 1e7f,
        };
        user.Init();
        _model = new ModelHuman(user);
        _location.Models.Add(_model);
        _model.Reset();

        // ModelHuman.Play(SpawnRunner): the rest pose's NPivot (here: the picked pivot node) on the spawn point, then
        // the spawn's move, which starts from that pose.
        var parts = spawn.Animation.Split('|');
        var reaction = AnimationLoader.ParseReaction(parts.Length >= 2 ? parts : new[] { parts[0], "0" });
        string pivot = _model.GetNode(spawn.PivotNode) != null ? spawn.PivotNode : "NPivot";
        _model.ControllerPhysics.NodeReset();
        _model.Position(new Vector3(spawn.Position, 0), pivot);
        _model.PlayAnimation(reaction);
        _model.IsEnabled = true;

        // The camera follows the model's Camera node (in the game a level-start trigger hands it over).
        _camera = new LocationCamera();
        _camera.Init();
        _camera.StartPosition(_model.ModelObject.CameraNode.Start);
        _camera.Node = _model.ModelObject.CameraNode;

        _drawMap = drawModel.NodeNamesOrdered.Select(name => _model.ModelObject.GetNode(name)?.Id ?? -1).ToArray();
        _lastTime = _clock.Elapsed.TotalSeconds;
        _accumulator = 0;
    }

    public void Clear()
    {
        _model = null;
        _location = null;
        _camera = null;
        _drawMap = null;
        LastSpawn = null;
        if (LevelMainController.current == _controller)
            LevelMainController.current = null!;
        _controller = null;
        Model.ModelsAll.Clear();
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
        if (_model == null || _location == null)
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

    /// <summary>One game tick: the camera, then the model (LevelMainController.Render).</summary>
    public void Tick()
    {
        if (_model == null || _location == null)
            return;
        LevelMainController.current = _controller!;
        _camera?.UpdatePosition();
        _location.Render();
    }

    /// <summary>An arrow key was pressed (the game reacts to presses, not to keys held down).</summary>
    public void Press(KeyboardKey key)
    {
        if (_model == null || _model.IsDeath)
            return;
        _model.ControllerKeys.SetKeyVariable(new KeyVariables(key));
    }

    // ================= DRAWING ================= //

    /// <summary>The character's nodes in level coordinates, in the order of the drawn model (runner.xml).</summary>
    public Vector3[]? DrawNodes()
    {
        if (_model == null || _drawMap == null)
            return null;
        var nodes = _model.ModelObject.NodesAll;
        var result = new Vector3[_drawMap.Length];
        for (int i = 0; i < _drawMap.Length; i++)
        {
            int id = _drawMap[i];
            result[i] = id >= 0 && id < nodes.Count ? nodes[id].Start : default;
        }
        return result;
    }

    /// <summary>Where the game's camera looks (it follows the model's Camera node smoothly), in level coordinates.</summary>
    public Avalonia.Point? CameraPosition => _camera == null ? null : new Avalonia.Point(_camera.X, _camera.Y);

    /// <summary>The model's Camera node, in level coordinates.</summary>
    public Avalonia.Point? CameraNode => _model?.ModelObject.CameraNode.Node is { } node ? new Avalonia.Point(node.Start.X, node.Start.Y) : null;
}
