using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia.Media;
using Vectorier.Gameplay;
using Vectorier.Gameplay.Rl;
using Vectorier.Services.Assets;
using Vectorier.Services.IO;
using Vectorier.Services.Projects;

namespace Vectorier.Services.Game;

/// <summary>
/// The game inside the editor (Play, Hunter Mode, Play Here, and the Move Visualizer's Gameplay mode). The game
/// (Gameplay folder) reads its files from <see cref="GameFiles"/>: they are read from the active project through the
/// editor's file system (again only when they changed), the level is the editor's own XML, and the textures are the
/// editor's bitmaps. Only one game runs at a time (the game's state is static).
/// </summary>
public sealed class GameHost
{
    public static GameHost Current { get; } = new();

    private VectorGame? _game;
    private string? _projectPath;
    private readonly Dictionary<string, DateTimeOffset?> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private string? _movesSignature;
    private readonly HashSet<string> _missingTextures = new(StringComparer.OrdinalIgnoreCase);
    private ProjectContext? _context;

    /// <summary>A level is being played in the Game View.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>The game (for the Move Visualizer, which runs it without its view).</summary>
    public VectorGame? Game => _game;

    /// <summary>The level played (and the mode), for the status bar.</summary>
    public string? LevelName { get; private set; }
    public bool IsHunterMode { get; private set; }

    /// <summary>Raised when a game starts or stops.</summary>
    public event Action? RunningChanged;

    /// <summary>The game's F11: the Game View should fill the editor's window (or stop doing so).</summary>
    public event Action? FullScreenToggled;

    /// <summary>Set by the Game View: whether it fills the window.</summary>
    public bool IsFullScreen { get; set; }

    /// <summary>Another user of the game (the Move Visualizer) is told to let go when a level starts.</summary>
    public event Action? Taken;

    private GameHost()
    {
        Raylib.FullScreenRequested += () => FullScreenToggled?.Invoke();
        Raylib.IsFullScreen = () => IsFullScreen;
        VectorGame.LoadSettingsText = () => AppServices.Platform?.Settings.Get(SettingsKey) ?? string.Empty;
        VectorGame.SaveSettingsText = text => AppServices.Platform?.Settings.Set(SettingsKey, text);
        Gameplay.Audio.GameAudio.CreateOutput = () => AppServices.Platform?.CreateAudioOutput();
        GameFiles.TextureMissing = name =>
        {
            lock (_missingTextures) _missingTextures.Add(name);
        };
        GameFiles.MusicMissing = name => _ = LoadMusicAsync(name);
    }

    private const string SettingsKey = "GameDebugSettings";

    // ================= STARTING ================= //

    /// <summary>
    /// Plays <paramref name="levelXml"/> (the level as the editor writes it) in the Game View. Null when it started,
    /// otherwise why it could not.
    /// </summary>
    public async Task<string?> StartAsync(ProjectInfo project, string levelName, byte[] levelXml, bool hunterMode)
    {
        Stop();
        Taken?.Invoke();
        try
        {
            bool projectChanged = await PrepareAsync(project) | _projectChangedPending;
            _projectChangedPending = false;
            GameFiles.Set(VPath.Combine("xmlroot", "levels", levelName + ".xml"), levelXml);
            bool loadMoves = await PrepareMovesAsync(project);

            using (Invariant())
            {
                GameState.ResetForNewGame(projectChanged);
                lock (_missingTextures) _missingTextures.Clear();
                _game ??= new VectorGame();
                VectorGame.Instance = _game;
                ResourceManager.LoadResources();
                _game.Start(levelName, hunterMode, loadMoves);
            }

            // Textures the level uses that the editor had not loaded yet: loaded, then the level starts again with them.
            string[] missing;
            lock (_missingTextures) missing = _missingTextures.ToArray();
            if (missing.Length > 0 && _context != null)
            {
                await Task.WhenAll(missing.Select(name => _context.Textures.GetAsync(name)));
                using (Invariant())
                {
                    GameState.ResetForNewGame(false);
                    ResourceManager.LoadResources();
                    _game.Start(levelName, hunterMode, false);
                }
            }

            LevelName = levelName;
            IsHunterMode = hunterMode;
            IsRunning = true;
            RunningChanged?.Invoke();
            return null;
        }
        catch (Exception ex)
        {
            Release();
            Stop();
            return ex.Message;
        }
    }

    /// <summary>Leaves the game (the Game View shows the level's camera again).</summary>
    public void Stop()
    {
        // Only a level played in the Game View: the Move Visualizer's runner (the same game state) is its own.
        if (IsRunning)
            Release();
        Raylib.ReleaseAllKeys();
        Gameplay.Audio.GameAudio.Mixer.StopAll();
        bool was = IsRunning;
        IsRunning = false;
        LevelName = null;
        if (was)
            RunningChanged?.Invoke();
    }

    /// <summary>The game lets go of its level (its settings saved, its sounds stopped, its state reset).</summary>
    private void Release()
    {
        using (Invariant())
        {
            _game?.Close();
            GameState.ResetForNewGame(false);
        }
    }

    // ================= THE MOVE VISUALIZER ================= //

    /// <summary>The Move Visualizer's level (its Platforms, Trapezoids and Areas) and its runner, in GameFiles.</summary>
    public const string MoveVisualizerLevel = "__movevisualizer";
    public const string MoveVisualizerModel = "__movevisualizer_runner.xml";

    /// <summary>
    /// Readies the game for the Move Visualizer's Gameplay mode: a level played in the Game View stops (the game is
    /// one), the project's moves are read and parsed (again only when they changed) and runner.xml is the model.
    /// </summary>
    public async Task PrepareMoveVisualizerAsync(ProjectInfo project, byte[] runner)
    {
        Stop();
        EnsureProject(project);
        GameFiles.Set(VPath.Combine("models", MoveVisualizerModel), runner);
        if (await PrepareMovesAsync(project))
        {
            try
            {
                await Task.Run(() =>
                {
                    using (Invariant())
                        VectorGame.LoadMoves();
                });
            }
            catch
            {
                _movesSignature = null; // parsed again next time
                throw;
            }
        }
    }

    // ================= FRAMES ================= //

    private double _lastFrame;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private int _frames;
    private double _fpsStart;
    private int _fps = 60;

    /// <summary>One frame of the game into the Game View (called from its Render, once per display frame).</summary>
    public void Frame(DrawingContext context, int width, int height)
    {
        if (!IsRunning || _game == null)
            return;
        double now = _clock.Elapsed.TotalSeconds;
        double dt = _lastFrame == 0 ? 1.0 / 60 : now - _lastFrame;
        _lastFrame = now;
        _frames++;
        if (now - _fpsStart >= 1)
        {
            _fps = (int)Math.Round(_frames / (now - _fpsStart));
            _frames = 0;
            _fpsStart = now;
        }
        Raylib.SetFrameTiming((float)dt, _fps);
        using (Invariant())
            _game.Frame(context, width, height);
    }

    // ================= INPUT (from the Game View) ================= //

    public void KeyDown(KeyboardKey key) => Raylib.KeyDown(key);
    public void KeyUp(KeyboardKey key) => Raylib.KeyUp(key);
    public void ReleaseKeys() => Raylib.ReleaseAllKeys();

    // ================= FILES ================= //

    /// <summary>
    /// Reads what the game needs from the project into <see cref="GameFiles"/>: the XML (trigger templates, object
    /// sets, models, localization), the sounds and the trick icons with their content; the textures, animated
    /// texture images and music by name only (textures are the editor's bitmaps, music is read when played).
    /// Files already read and unchanged are kept. True when it is another project than last time.
    /// </summary>
    private async Task<bool> PrepareAsync(ProjectInfo project)
    {
        var fs = AppServices.FileSystem;
        bool projectChanged = EnsureProject(project);
        _context = ProjectContext.For(project);
        if (!_context.Textures.IsIndexed)
            await _context.Textures.RefreshIndexAsync();
        var textures = _context.Textures;
        GameFiles.TextureProvider = name => textures.TryGet(name)?.Bitmap;

        string root = project.Path;
        string Rel(string path) => path.StartsWith(root + "/", StringComparison.Ordinal) ? path[(root.Length + 1)..] : path;

        async Task ListAsync(string folder, bool recursive, Func<string, bool> withContent, Func<string, bool>? include = null)
        {
            string path = VPath.Combine(root, folder);
            if (!await fs.DirectoryExistsAsync(path))
                return;
            foreach (var file in await fs.GetFilesAsync(path, recursive))
            {
                string rel = Rel(file);
                if (include != null && !include(rel))
                    continue;
                if (!withContent(rel))
                {
                    if (!GameFiles.Contains(rel))
                        GameFiles.Set(rel, null);
                    continue;
                }
                var time = await fs.GetLastWriteTimeAsync(file);
                if (_loaded.TryGetValue(rel, out var known) && known == time && GameFiles.HasContent(rel))
                    continue;
                GameFiles.Set(rel, await fs.ReadAllBytesAsync(file));
                _loaded[rel] = time;
            }
        }

        static bool Xml(string p) => p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
        // The other levels are not needed: the played one is the editor's XML.
        await ListAsync("xmlroot", true, Xml, p => !p.StartsWith("xmlroot/levels/", StringComparison.OrdinalIgnoreCase));
        await ListAsync("models", false, Xml);
        await ListAsync("localization", false, Xml);
        await ListAsync("sounds", false, _ => true);
        await ListAsync(VPath.Combine("icons", "tricks"), false, _ => true);
        await ListAsync("icons", false, _ => false);
        await ListAsync("textures", false, _ => false);
        await ListAsync("animatedtextures", true, p => p.EndsWith(".plist", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        await ListAsync("music", false, _ => false);
        return projectChanged;
    }

    /// <summary>Another project than last time: nothing read for the previous one is kept. True when it changed.</summary>
    private bool EnsureProject(ProjectInfo project)
    {
        if (string.Equals(_projectPath, project.Path, StringComparison.Ordinal))
            return false;
        GameFiles.Clear();
        _loaded.Clear();
        _movesSignature = null;
        _projectPath = project.Path;
        VectorPaths.ProjectName = project.Name;
        _projectChangedPending = true;
        return true;
    }

    // A project change seen by the Move Visualizer first: the next level played still resets the project's caches.
    private bool _projectChangedPending;

    /// <summary>moves.xml and its animations, read again (and parsed again) only when one of them changed.</summary>
    private async Task<bool> PrepareMovesAsync(ProjectInfo project)
    {
        var fs = AppServices.FileSystem;
        string movesPath = VPath.Combine(project.AnimationsFolder, "moves.xml");
        if (!await fs.FileExistsAsync(movesPath))
            throw new System.IO.FileNotFoundException(Localization.Loc.F("moves.xml not found: {0}", movesPath));
        byte[] moves = await fs.ReadAllBytesAsync(movesPath);

        string dataFolder = VPath.Combine(project.AnimationsFolder, "data");
        var onDisk = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (await fs.DirectoryExistsAsync(dataFolder))
            foreach (var file in await fs.GetFilesAsync(dataFolder))
                onDisk.TryAdd(VPath.GetFileName(file), file);
        var bins = MoveFileNames(moves).Where(onDisk.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var signature = new System.Text.StringBuilder(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(moves)));
        foreach (var bin in bins)
            signature.Append('|').Append(bin).Append('@').Append(await fs.GetLastWriteTimeAsync(onDisk[bin]));
        string sig = signature.ToString();
        if (sig == _movesSignature)
            return false;

        GameFiles.Set("animations/moves.xml", moves);
        foreach (var bin in bins)
            GameFiles.Set("animations/data/" + bin, await fs.ReadAllBytesAsync(onDisk[bin]));
        _movesSignature = sig;
        return true;
    }

    /// <summary>Reload Moves (the game's button): the files are read again first, then the game reloads them.</summary>
    public async Task ReloadMovesAsync(ProjectInfo project)
    {
        _movesSignature = null;
        await PrepareMovesAsync(project);
        if (IsRunning && LevelMainController.current != null)
            using (Invariant())
                LevelMainController.current.ReloadMovesButton();
    }

    private static IEnumerable<string> MoveFileNames(byte[] moves)
    {
        XElement? root;
        try
        {
            root = Xml.XmlHelpers.ParseDocument(moves).Root;
        }
        catch
        {
            yield break;
        }
        foreach (var move in root?.Element("Moves")?.Elements() ?? Enumerable.Empty<XElement>())
            if ((string?)move.Attribute("FileName") is { Length: > 0 } file)
                yield return file;
    }

    /// <summary>Music is large: read the first time a level plays it, then started (if that level still plays it).</summary>
    private async Task LoadMusicAsync(string name)
    {
        if (_projectPath == null)
            return;
        var fs = AppServices.FileSystem;
        string folder = VPath.Combine(_projectPath, "music");
        try
        {
            foreach (var file in await fs.GetFilesAsync(folder))
            {
                if (!string.Equals(VPath.GetFileNameWithoutExtension(file), name, StringComparison.OrdinalIgnoreCase))
                    continue;
                GameFiles.Set(VPath.Combine("music", VPath.GetFileName(file)), await fs.ReadAllBytesAsync(file));
                if (IsRunning && SoundsManager.CurrentMusicName == name)
                    using (Invariant())
                        SoundsManager.PlayMusic(name);
                return;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Music " + name + ": " + ex.Message);
        }
    }

    // ================= CULTURE ================= //

    /// <summary>The game reads and writes numbers with the invariant culture (it ran with InvariantGlobalization).</summary>
    public static IDisposable Invariant() => new CultureScope();

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _ui = CultureInfo.CurrentUICulture;

        public CultureScope()
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _ui;
        }
    }
}
