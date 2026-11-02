using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Vectorier.Controls.Scene;
using Vectorier.Models.Scene;
using Vectorier.Services.Assets;
using Vectorier.Services.Localization;
using Vectorier.Services.Triggers;
using Vectorier.ViewModels;
using Vectorier.ViewModels.Editor;
using Vectorier.ViewModels.ProjectManager;

namespace Vectorier.Services.Tutorial;

/// <summary>The tutorial's steps, from Project Selection to a saved, playable level.</summary>
public static partial class TutorialGuide
{
    private const string StarterLevel = "DOWNTOWN_STORY_01";
    private const string BackgroundTexture = "v_bg";
    private const string ObstacleFile = "objects.xml";
    private const string ObstacleName = "SmallPipeT";

    // Where things go. Level coordinates as elements store them (Y down): the Inspector shows Y the other way up,
    // so (494, 2) there is (494, -2) here. Placed elements are moved to these spots whatever the user clicked.
    private static readonly Avalonia.Point SpawnAt = new(0, 0);
    private static readonly Avalonia.Point CameraAt = new(494, -2);
    private static readonly Avalonia.Point BackgroundAt = new(-1000, -500);
    private static readonly Avalonia.Point ObstacleAirAt = new(843.419, -24.766);
    private static readonly Avalonia.Point ObstacleGroundAt = new(593.891, 131);
    private static readonly Avalonia.Point BonusAt = new(647.771, 53.957);
    private static readonly Avalonia.Rect PlatformBox = new(-803, 153, 2000, 500);
    private static readonly Avalonia.Rect TriggerBox = new(474.839, -172.345, 41.739, 396.522);

    /// <summary>The Move Visualizer's runner is held on this frame as a size reference.</summary>
    private const int ReferenceFrame = 6;

    // What the user made along the way.
    private static string? _projectName;
    private static int _count;
    private static HashSet<SceneElement> _before = new();
    private static SpawnElement? _spawn;
    private static CameraElement? _camera;
    private static ImageElement? _background;
    private static PlatformElement? _platform;
    private static ObjectElement? _obstacle;
    private static TriggerElement? _trigger;
    private static string _triggerName = string.Empty;
    private static int _signal;

    private static ProjectSelectionViewModel? Selection => AppServices.Shell?.CurrentScreen as ProjectSelectionViewModel;
    private static ProjectManagerViewModel? Manager => AppServices.Shell?.CurrentScreen as ProjectManagerViewModel;
    private static EditorViewModel? Editor => AppServices.Shell?.CurrentScreen as EditorViewModel;

    private static T? OpenDialog<T>() where T : class => DialogService.OpenDialogs.OfType<T>().LastOrDefault();

    // ================= STEP BUILDERS ================= //

    /// <summary>Explains something; the user clicks Next.</summary>
    private static TutorialStep Info(string title, string text, Func<IEnumerable<Control?>>? targets = null, bool dialog = false) =>
        new() { Title = title, Text = text, Targets = targets, InDialog = dialog };

    /// <summary>Asks the user to do something in the lit places; ends when it is done.</summary>
    private static TutorialStep Act(string title, string text, Func<bool> done, Func<IEnumerable<Control?>>? targets, bool dialog = false) =>
        new() { Title = title, Text = text, Targets = targets, Interactive = true, Done = done, InDialog = dialog };

    /// <summary>An explanation whose lit places can be used while it is shown (Next when done).</summary>
    private static TutorialStep Interactive(TutorialStep step)
    {
        step.Interactive = true;
        return step;
    }

    /// <summary>Lets the user work freely in the lit places, then click Next.</summary>
    private static TutorialStep Free(string title, string text, Func<IEnumerable<Control?>>? targets) =>
        new() { Title = title, Text = text, Targets = targets, Interactive = true };

    // Editor places.
    private static Control? Scene() => Find<SceneView>(s => s.Name == "Scene");
    private static Control? GameScene() => Find<SceneView>(s => s.Name == "GameScene");
    private static Control? ElementButton(ElementType type) => Find<Button>(b => b.Tag is ElementTypeInfo info && info.Type == type);
    private static Control? ProjectWindow() => Find<Views.Editor.ProjectWindowView>();
    private static Control? Inspector() => Find<Views.Editor.InspectorView>();
    private static Control? Hierarchy() => Find<Views.Editor.HierarchyView>();
    private static Control? PlayButton() => ByCommand(Editor?.PlayLevelCommand, "tool");

    private static bool IsElement(ElementType type) => Editor?.ActiveElementType?.Type == type;

    private static IEnumerable<SceneElement> Elements() => Editor?.Document.AllElements() ?? Enumerable.Empty<SceneElement>();

    /// <summary>Remembers what is in the level, so the next element the user places can be found.</summary>
    private static void Snapshot() => _before = Elements().ToHashSet();

    private static T? Placed<T>(Func<T, bool>? predicate = null) where T : SceneElement =>
        Elements().OfType<T>().FirstOrDefault(e => !_before.Contains(e) && (predicate == null || predicate(e)));

    /// <summary>After placing one: stop placing, select it.</summary>
    private static void Finish(SceneElement element)
    {
        var editor = Editor;
        if (editor == null)
            return;
        editor.ExitPlacement();
        editor.Select(element);
    }

    private static void KeepSelected(SceneElement? element)
    {
        var editor = Editor;
        if (editor == null || element == null || element.Parent == null && !editor.Document.Roots.Contains(element))
            return;
        if (editor.Selection.Count != 1 || !ReferenceEquals(editor.Selection[0], element))
            editor.Select(element);
    }

    private static PlatformElement? _clamped;

    /// <summary>
    /// From the size step to the end of the tutorial, the platform can never be bigger than its box: every change
    /// (Rect / Scale drags, the Inspector) is cut back at once, also while a drag is still going.
    /// </summary>
    private static void ClampPlatform(PlatformElement platform)
    {
        UnclampPlatform();
        _clamped = platform;
        platform.PropertyChanged += OnClampedChanged;
        OnClampedChanged(platform, new System.ComponentModel.PropertyChangedEventArgs(nameof(SceneElement.Width)));
    }

    private static void UnclampPlatform()
    {
        if (_clamped != null)
            _clamped.PropertyChanged -= OnClampedChanged;
        _clamped = null;
    }

    private static void OnClampedChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not PlatformElement p)
            return;
        if (e.PropertyName == nameof(SceneElement.Width) && p.Width > PlatformBox.Width)
            p.Width = PlatformBox.Width;
        else if (e.PropertyName == nameof(SceneElement.Height) && p.Height > PlatformBox.Height)
            p.Height = PlatformBox.Height;
    }

    private static void PlaceAt(SceneElement element, Avalonia.Point at)
    {
        if (element.X != at.X)
            element.X = at.X;
        if (element.Y != at.Y)
            element.Y = at.Y;
    }

    /// <summary>Scrolls the Scene to an area when it is not already in view (zoom unchanged).</summary>
    private static void Look(Avalonia.Rect area)
    {
        if (Scene() is not SceneView view || view.Bounds.Width <= 0)
            return;
        var view1 = view.ScreenToLevel(new Avalonia.Point(40, 40));
        var view2 = view.ScreenToLevel(new Avalonia.Point(view.Bounds.Width - 40, view.Bounds.Height - 40));
        var visible = new Avalonia.Rect(view1, view2).Normalize();
        if (!visible.Contains(area.TopLeft) || !visible.Contains(area.BottomRight))
            view.CenterOn(area.Center);
    }

    private static void ClearFilters() => Editor?.ProjectWindow.SetTutorialFilter(null, null);

    /// <summary>Plays the level in the game, and waits until the game is closed again.</summary>
    private static TutorialStep Play(string title, string text, Func<IEnumerable<Control?>>? extraTargets = null)
    {
        int launches = 0;
        return new TutorialStep
        {
            Title = title,
            Text = text,
            Interactive = true,
            Targets = () => new[] { PlayButton(), Scene(), Named("MoveTool"), Named("RotateTool"), Named("ScaleTool"), Named("RectTool") }.Concat(extraTargets?.Invoke() ?? Enumerable.Empty<Control?>()),
            Skip = () => AppServices.Platform?.CanLaunchGame != true,
            Enter = () => launches = TutorialSignals.GameLaunches,
            Done = () => TutorialSignals.GameLaunches > launches && AppServices.Platform?.IsGameRunning != true,
        };
    }

    // ================= THE STEPS ================= //

    private static List<TutorialStep> BuildSteps()
    {
        var steps = new List<TutorialStep>();
        // Steps are grouped in short sections; the card counts the steps of the current section only.
        string section = string.Empty;
        void Section(string name) => section = name;
        void Add(TutorialStep step)
        {
            step.Section = section;
            steps.Add(step);
        }

        // ---------------- Project Selection ----------------
        Section("Projects");

        Add(Info("Projects",
            "Everything you make lives in a project.\n\nA project keeps your levels, textures and objects apart from the game's own files. You can have as many as you like.",
            () => Many(Find<ListBox>(l => l.Name == "ProjectListBox"))));

        Add(new TutorialStep
        {
            Title = "Create a project",
            Text = "Let's make your first one.\n\nClick + (Add Project) and give it any name you like.",
            Interactive = true,
            Targets = () => Many(ByCommand(Selection?.AddProjectCommand)),
            Enter = () => _count = Selection?.ProjectList.Count ?? 0,
            Done = () =>
            {
                var selection = Selection;
                if (selection == null || selection.ProjectList.Count <= _count || selection.SelectedProject == null)
                    return false;
                _projectName = selection.SelectedProject.Name;
                return true;
            },
        });

        Add(new TutorialStep
        {
            Title = "Select your project",
            DynamicText = () => Loc.F("There it is.\n\nClick {0} to select it.", _projectName ?? string.Empty),
            Interactive = true,
            Targets = () => Many(ItemOf(Selection?.ProjectList.FirstOrDefault(p => p.Name == _projectName))),
            Enter = () =>
            {
                if (Selection is { } s)
                    s.SelectedProject = null;
            },
            Done = () => Selection?.SelectedProject?.Name == _projectName,
        });

        Add(Info("Project details",
            "These are your project's details.\n\nClick the picture to give it a thumbnail. You can change the name, author and description any time.",
            () => Many(Named("DetailsPanel"))));

        Add(Act("Open the project",
            "Click Open Selected Project to start working on it.",
            () => Manager != null,
            () => Many(ByCommand(Selection?.OpenProjectCommand))));

        // ---------------- Project Manager ----------------
        Section("The Project Manager");

        Add(Info("The Project Manager",
            "This is the Project Manager.\n\nEverything in your project is here, sorted into the tabs on the left.",
            () => Many(Find<ListBox>(l => l.Classes.Contains("nav")))));

        Add(new TutorialStep
        {
            Title = "Levels",
            Text = "Levels are the stages you play, grouped by location and mode.\n\nClick DOWNTOWN_STORY_01.",
            Interactive = true,
            Targets = () => Many(ItemOf(Manager?.Levels.Items.FirstOrDefault(i => i.Name == StarterLevel))),
            Enter = () =>
            {
                if (Manager is { } m)
                {
                    m.SelectedPage = m.Levels;
                    m.Levels.SelectedItem = null;
                }
            },
            Done = () => Manager?.Levels.SelectedItem?.Name == StarterLevel,
        });

        Add(Info("Level properties",
            "This side shows the selected level: its thumbnail, in-game name, location and track settings.\n\nYou can change any of it here.",
            () => Many(AncestorPanel(ByCommand(Manager?.Levels.OpenInEditorCommand, "accent")))));

        Add(Info("Edit Level",
            "Edit Level opens the selected level in the editor.\n\nWe'll use it in a moment.",
            () => Many(ByCommand(Manager?.Levels.OpenInEditorCommand, "accent"))));

        Add(Info("The Editor button",
            "The Editor button opens the editor with a fresh, empty level.",
            () => Many(ByCommand(Manager?.OpenEmptyEditorCommand))));

        // The other categories: click, then a short explanation.
        Section("Categories");
        var categories = new (int Index, string Text)[]
        {
            (1, "Locations group levels together, like Downtown or Construction.\n\nAdd locations here and change the order of their levels."),
            (2, "Textures are the pictures your levels are made of.\n\nBrowse them here, or import your own."),
            (3, "Animations are the characters' moves: the moves file, plus one animation file per move.\n\nSelect one to watch it on the character."),
            (4, "Tricks are special moves players buy in the shop.\n\nSet their names, icons and prices here."),
            (5, "Gears are the other items in the shop.\n\nTheir names, icons and prices are set here too."),
            (6, "Models are the game's characters, like the runner and the hunter."),
            (7, "Music holds the songs your levels can play."),
            (8, "Sounds holds the game's sound effects."),
            (9, "Videos are cutscenes that can play at the start or end of a level."),
            (10, "XML Files are the project's data files.\n\nVectorier writes them for you, so you'll rarely need to open them."),
        };
        // One step per category: click it, then the same card explains it (Next once it is open).
        foreach (var (index, text) in categories)
        {
            int i = index;
            PmPageViewModel? Page() => Manager is { } m && i < m.Pages.Count ? m.Pages[i] : null;
            bool IsOpen() => Manager is { } m && Page() is { } page && ReferenceEquals(m.SelectedPage, page);
            Add(new TutorialStep
            {
                Title = "Categories",
                DynamicText = () => IsOpen() ? Loc.T(text) : Loc.F("Click {0}.", Page()?.Title ?? string.Empty),
                Targets = () => IsOpen()
                    ? Many(ItemOf(Page()), Find<ContentControl>(c => c is not ListBoxItem && ReferenceEquals(c.Content, Page())))
                    : Many(ItemOf(Page())),
                // Clickable until it is open; then the card explains it.
                InteractiveWhen = () => !IsOpen(),
                NextWhen = IsOpen,
            });
        }

        Section("Your Level");
        Add(Act("Back to Levels",
            "That's all of them.\n\nClick Levels to go back.",
            () => Manager is { } m && ReferenceEquals(m.SelectedPage, m.Levels),
            () => Many(ItemOf(Manager?.Levels))));

        Add(new TutorialStep
        {
            Title = "Create a level",
            Text = "Now let's make a level of your own.\n\nClick + (New Level).",
            Interactive = true,
            Targets = () => Many(ByCommand(Manager?.Levels.AddLevelCommand)),
            Enter = () =>
            {
                // New levels go to the location of the selected level: Downtown.
                Manager?.Levels.SelectLevelByName(StarterLevel);
                _count = Manager?.Levels.Items.Count ?? 0;
            },
            Done = () => Manager is { } m && m.Levels.Items.Count > _count && m.Levels.SelectedItem is { } item && item.Name != StarterLevel,
        });

        Add(new TutorialStep
        {
            Title = "Open your level",
            DynamicText = () => Loc.F("Your new level, {0}, is ready.\n\nClick Edit Level to open it.", Manager?.Levels.SelectedItem?.Name ?? string.Empty),
            Interactive = true,
            Targets = () => Many(ByCommand(Manager?.Levels.OpenInEditorCommand, "accent")),
            Done = () => Editor != null,
        });

        // ---------------- Editor tour ----------------
        Section("The Level Editor");

        Add(Info("The Level Editor",
            "This is the Level Editor, where you'll build your level.\n\nLet's take a quick look around."));
        Add(Info("The top menu",
            "The top menu has every command.\n\nSaving, playing and Level Settings are under File.",
            () => Many(Find<Menu>())));
        Add(Info("The toolbar",
            "The toolbar has the tools for moving and resizing things.\n\nUndo, Save and Level Settings are here too.",
            () => Many(Find<Border>(b => b.Classes.Contains("toolbar") && b.FindAncestorOfType<Views.Editor.EditorView>() != null && b.FindAncestorOfType<Views.Editor.TriggerEditorView>() == null))));
        Add(Info("The Element Bar",
            "The Element Bar holds everything you can put in a level.\n\nClick one to start placing it. The eye next to it hides or shows that kind in the Scene.",
            () => Many(Named("ElementBar"))));
        Add(Info("The Hierarchy",
            "The Hierarchy lists everything in your level.\n\nClick a name to select it.",
            () => Many(Hierarchy())));
        Add(Info("The Project window",
            "The Project window shows what you can place for the chosen element: textures, objects, platform styles and more.",
            () => Many(ProjectWindow())));
        Add(Info("The Inspector",
            "The Inspector shows the settings of whatever you select.\n\nPosition, size, layer, and so on.",
            () => Many(Inspector())));
        // Try it: the Scene takes the mouse here (Next when done).
        Add(Interactive(Info("The Scene",
            "This is the Scene: your level.\n\nGive it a try. Hold the right mouse button and drag to look around, and scroll to zoom.\n\nClick Next when you're ready.",
            () => Many(Scene()))));

        // ---------------- Spawn ----------------
        Section("Spawn");

        Add(Act("Add a spawn",
            "First, the runner needs somewhere to start.\n\nClick Spawn.",
            () => IsElement(ElementType.Spawn) && Editor?.PlacementType == ElementType.Spawn,
            () => Many(ElementButton(ElementType.Spawn))));
        Add(new TutorialStep
        {
            Title = "Place the spawn",
            Text = "Click the marked spot in the Scene to place it.",
            Interactive = true,
            Targets = () => Many(Scene()),
            Guides = () => new[] { new SceneGuide(Point: SpawnAt) },
            Enter = () =>
            {
                Snapshot();
                Look(new Rect(SpawnAt, SpawnAt));
            },
            Done = () =>
            {
                if (Placed<SpawnElement>() is not { } spawn)
                    return false;
                _spawn = spawn;
                PlaceAt(spawn, SpawnAt);
                Finish(spawn);
                return true;
            },
        });
        Add(Info("The spawn point",
            "That's the spawn point.\n\nThe runner appears here when the level starts.",
            () => Many(Scene())));
        Add(new TutorialStep
        {
            Title = "Select the spawn",
            Text = "Click the spawn to select it.\n\nClicking its name in the Hierarchy works too.",
            Interactive = true,
            Targets = () => Many(Scene(), Hierarchy()),
            Enter = () => Editor?.ClearSelection(),
            Done = () => Editor is { } e && _spawn != null && e.Selection.Contains(_spawn),
        });
        Add(new TutorialStep
        {
            Title = "Preview Animation",
            Text = "With the spawn selected, the Inspector has a Preview Animation button.\n\nClick it.",
            Interactive = true,
            Targets = () => Many(WithText<Button>("Preview Animation")),
            Tick = () => KeepSelected(_spawn),
            Done = () => Editor is { } e && e.IsMoveVisualizerTab && e.MoveVisualizer.IsPlaced,
        });
        Add(Info("The runner",
            "Here's the runner, playing its starting move from the spawn.\n\nThe Move Visualizer can play any move anywhere in the level. It's handy for checking jumps while you build.",
            () => Many(Scene(), Find<Views.Editor.MoveVisualizerView>())));
        Add(new TutorialStep
        {
            Title = "Back to the Inspector",
            Text = "We'll keep the runner there, frozen, so you can see how big things are next to it.\n\nClick the Inspector tab to go back.",
            Interactive = true,
            Targets = () => Many(WithText<RadioButton>("Inspector")),
            // Held still as a size reference for the rest of the tutorial.
            Enter = () =>
            {
                if (Editor?.MoveVisualizer is { IsPlaced: true } visualizer)
                    visualizer.Frame = ReferenceFrame;
            },
            Done = () => Editor?.IsInspectorTab == true,
        });

        // ---------------- Camera ----------------
        Section("Camera");

        Add(Act("Add a camera",
            "Next, the camera.\n\nClick Camera.",
            () => IsElement(ElementType.Camera) && Editor?.PlacementType == ElementType.Camera,
            () => Many(ElementButton(ElementType.Camera))));
        Add(new TutorialStep
        {
            Title = "Place the camera",
            Text = "Click the marked spot to place it.",
            Interactive = true,
            Targets = () => Many(Scene()),
            Guides = () => new[] { new SceneGuide(Point: CameraAt) },
            Enter = () =>
            {
                Snapshot();
                Look(new Rect(CameraAt, CameraAt));
            },
            Done = () =>
            {
                if (Placed<CameraElement>() is not { } camera)
                    return false;
                _camera = camera;
                PlaceAt(camera, CameraAt);
                Finish(camera);
                return true;
            },
        });
        Add(new TutorialStep
        {
            Title = "The Game view",
            Text = "The camera decides what the player sees when the level starts.\n\nClick the Game tab to look through it.",
            Interactive = true,
            Targets = () => Many(WithText<RadioButton>("Game")),
            Tick = () => KeepSelected(_camera),
            Done = () => Editor?.IsGameViewTab == true,
        });
        Add(Info("The Game view",
            "This is the view from the camera, just like in the game.\n\nIt's only for looking. You build in the Scene.",
            () => Many(GameScene())));
        Add(Act("Back to the Scene",
            "Click the Scene tab to go back.",
            () => Editor?.IsSceneViewTab == true,
            () => Many(WithText<RadioButton>("Scene"))));

        // ---------------- Background ----------------
        Section("Background");

        Add(Info("A background",
            "The level looks a bit bare.\n\nLet's add a background."));
        Add(new TutorialStep
        {
            Title = "Images",
            Text = "Pictures go into a level as Images.\n\nClick Images.",
            Interactive = true,
            Targets = () => Many(ElementButton(ElementType.Image)),
            Enter = () => Editor?.ProjectWindow.SetTutorialFilter(
                f => Editor!.ProjectWindow.FolderHoldsTexture(f, BackgroundTexture),
                i => string.Equals(i.Texture?.Name, BackgroundTexture, StringComparison.OrdinalIgnoreCase)),
            Done = () => IsElement(ElementType.Image),
        });
        Add(Act("Pick the background",
            "The Project window shows your textures. We've hidden all but one for now.\n\nClick v_bg.",
            () => Editor is { } e && string.Equals(e.ProjectWindow.SelectedItem?.Texture?.Name, BackgroundTexture, StringComparison.OrdinalIgnoreCase) && e.PlacementType == ElementType.Image,
            () => Many(ProjectWindow())));
        Add(new TutorialStep
        {
            Title = "Place the background",
            Text = "Click anywhere in the Scene to place it.",
            Interactive = true,
            Targets = () => Many(Scene()),
            Enter = Snapshot,
            Done = () =>
            {
                if (Placed<ImageElement>(i => string.Equals(i.ClassName, BackgroundTexture, StringComparison.OrdinalIgnoreCase)) is not { } image)
                    return false;
                _background = image;
                Finish(image);
                ClearFilters();
                return true;
            },
            Leave = ClearFilters,
        });
        Add(new TutorialStep
        {
            Title = "Layers",
            Text = "Things far away should scroll slower than the ground. That's what layers are for.\n\nIn the Inspector, set Layer to Background.",
            Interactive = true,
            Targets = () => Many(Find<ComboBox>(c => ReferenceEquals(c.ItemsSource, InspectorViewModel.LayerNames))),
            Tick = () => KeepSelected(_background),
            Done = () => _background?.Layer == "Background",
            Leave = () =>
            {
                // Wherever it was placed, it goes where it covers the start of the level.
                if (_background != null)
                    PlaceAt(_background, BackgroundAt);
            },
        });
        Add(Info("The background is set",
            "Done. The background now sits behind everything and scrolls slowly in the game.",
            () => Many(Scene(), Inspector())));

        // ---------------- Platform ----------------
        Section("Platform");

        Add(Info("A platform",
            "The runner has nothing to stand on yet.\n\nLet's add a platform: the ground it runs on."));
        Add(Act("Platforms",
            "Click Platform.",
            () => IsElement(ElementType.Platform),
            () => Many(ElementButton(ElementType.Platform))));
        Add(Act("Pick a style",
            "Platforms come in three styles: rough, smooth and block.\n\nClick rough.",
            () => Editor is { } e && e.ProjectWindow.SelectedItem?.PlatformSurface == PlatformSurfaceType.Rough && e.PlacementType == ElementType.Platform,
            () => Many(ProjectWindow())));
        Add(new TutorialStep
        {
            Title = "Place the platform",
            Text = "Click in the Scene to place it.\n\nYou can also drag to draw it at the size you want.",
            Interactive = true,
            Targets = () => Many(Scene()),
            Enter = Snapshot,
            Done = () =>
            {
                if (Placed<PlatformElement>() is not { } platform)
                    return false;
                _platform = platform;
                Finish(platform);
                return true;
            },
        });

        // Tools, with the platform kept selected.
        Section("Tools");
        void Tool(string title, string text, EditorTool tool, string toolName)
        {
            Add(new TutorialStep
            {
                Title = title,
                Text = text,
                Interactive = true,
                Targets = () => Many(Named(toolName)),
                Tick = () => KeepSelected(_platform),
                Done = () => Editor?.ActiveTool == tool,
            });
        }
        void Drag(string title, string text, Func<int> counter)
        {
            Add(new TutorialStep
            {
                Title = title,
                Text = text,
                Interactive = true,
                Targets = () => Many(Scene()),
                Enter = () => _signal = counter(),
                Tick = () => KeepSelected(_platform),
                Done = () => counter() > _signal,
            });
        }
        Tool("The Move tool", "Tools change what dragging does.\n\nPick the Move tool (or press Q).", EditorTool.Move, "MoveTool");
        Drag("Move it", "Drag the platform, or one of its arrows, then let go.", () => TutorialSignals.MoveDrags);
        Tool("The Scale tool", "Now the Scale tool (or press E).", EditorTool.Scale, "ScaleTool");
        Drag("Scale it", "Drag a handle to stretch the platform.", () => TutorialSignals.ScaleDrags);
        Tool("The Rect tool", "Last one: the Rect tool (or press T).", EditorTool.Rect, "RectTool");
        Drag("Resize it", "Drag an edge or a corner to resize the platform.", () => TutorialSignals.RectDrags);

        Add(new TutorialStep
        {
            Title = "Make room to run",
            Text = "The runner needs more room.\n\nDrag the right and bottom edges until the platform fills the orange box: 2000 by 500.\n\nYou can also type the size in the Inspector.",
            Interactive = true,
            Targets = () => Many(Scene(), Inspector()),
            Guides = () => new[] { new SceneGuide(Box: PlatformBox) },
            Enter = () =>
            {
                // The platform moves to where the rest of the level is built, and stays there.
                if (_platform != null)
                {
                    PlaceAt(_platform, PlatformBox.TopLeft);
                    ClampPlatform(_platform);
                }
                Look(PlatformBox);
            },
            Tick = () =>
            {
                KeepSelected(_platform);
                if (_platform != null)
                    PlaceAt(_platform, PlatformBox.TopLeft);
            },
            // Done once the size is reached and the drag is over (a drag still going would keep resizing it).
            Done = () => _platform != null && _platform.Width >= PlatformBox.Width - 0.5 && _platform.Height >= PlatformBox.Height - 0.5
                && Scene() is SceneView { IsTransforming: false },
        });

        // ---------------- Play ----------------

        Add(Play("Try it!",
            "Your level is playable now.\n\nClick Play to try it, then close the game to come back."));

        // ---------------- Obstacle ----------------
        Section("Obstacle");

        Add(Info("An obstacle",
            "Let's add something to jump over."));
        Add(new TutorialStep
        {
            Title = "Objects",
            Text = "Ready-made pieces like obstacles and buildings are Objects.\n\nClick Objects.",
            Interactive = true,
            Targets = () => Many(ElementButton(ElementType.Object)),
            Enter = () => Editor?.ProjectWindow.SetTutorialFilter(
                f => string.Equals(f.FileName, ObstacleFile, StringComparison.OrdinalIgnoreCase),
                i => i.Label == ObstacleName),
            Done = () => IsElement(ElementType.Object),
        });
        Add(Act("Pick an obstacle",
            "Objects are stored in XML files, listed on the left. The right side shows what's inside.\n\nClick SmallPipeT.",
            () => Editor is { } e && e.ProjectWindow.SelectedItem?.Label == ObstacleName && e.PlacementType == ElementType.Object,
            () => Many(ProjectWindow())));
        Add(new TutorialStep
        {
            Title = "Place the obstacle",
            Text = "Click the marked spot to place it.",
            Interactive = true,
            Targets = () => Many(Scene()),
            Guides = () => new[] { new SceneGuide(Point: ObstacleAirAt) },
            Enter = () =>
            {
                Snapshot();
                Look(new Rect(ObstacleAirAt, ObstacleAirAt));
            },
            Done = () =>
            {
                if (Placed<ObjectElement>(o => o.Name == ObstacleName || o.Reference?.Name == ObstacleName) is not { } obstacle)
                    return false;
                _obstacle = obstacle;
                PlaceAt(obstacle, ObstacleAirAt);
                Finish(obstacle);
                ClearFilters();
                return true;
            },
            Leave = ClearFilters,
        });
        Add(new TutorialStep
        {
            Title = "Snapping",
            Text = "It's floating! Snapping puts it right on the ground.\n\nHold C and point at the pipe: its corners light up. Drag a bottom corner onto the platform, into the orange box, and let go.",
            Interactive = true,
            Targets = () => Many(Scene()),
            // The pipe's own outline, at the spot where it should land.
            Guides = () => _obstacle == null
                ? Array.Empty<SceneGuide>()
                : new[] { new SceneGuide(Box: SceneRenderer.WorldBounds(_obstacle).Translate(ObstacleGroundAt - new Point(_obstacle.X, _obstacle.Y))) },
            Enter = () =>
            {
                _signal = TutorialSignals.PlatformSnapDrags;
                Look(new Rect(ObstacleGroundAt, ObstacleAirAt));
            },
            Done = () =>
            {
                if (TutorialSignals.PlatformSnapDrags <= _signal)
                    return false;
                if (_obstacle != null)
                    PlaceAt(_obstacle, ObstacleGroundAt);
                return true;
            },
        });
        Add(Free("Your turn",
            "Nice. Move it around a little if you like.\n\nClick Next when you're happy with it.",
            () => Many(Scene(), Named("MoveTool"), Named("RotateTool"), Named("ScaleTool"), Named("RectTool"))));
        Add(Play("Jump over it",
            "Play the level and jump over the pipe with the Up key.\n\nClose the game when you're done."));

        // ---------------- Item ----------------
        Section("Item");

        Add(new TutorialStep
        {
            Title = "Items",
            Text = "Things the runner picks up are Items.\n\nClick Item.",
            Interactive = true,
            Targets = () => Many(ElementButton(ElementType.Item)),
            Enter = () => Editor?.ProjectWindow.SetTutorialFilter(null, i => i.Label == "Bonus"),
            Done = () => IsElement(ElementType.Item),
        });
        Add(Act("Pick a bonus",
            "Click Bonus.",
            () => Editor is { } e && e.ProjectWindow.SelectedItem?.Label == "Bonus" && e.PlacementType == ElementType.Item,
            () => Many(ProjectWindow())));
        Add(new TutorialStep
        {
            Title = "Place the bonus",
            Text = "Click the marked spot to place it.",
            Interactive = true,
            Targets = () => Many(Scene()),
            Guides = () => new[] { new SceneGuide(Point: BonusAt) },
            Enter = () =>
            {
                Snapshot();
                Look(new Rect(BonusAt, BonusAt));
            },
            Done = () =>
            {
                if (Placed<ItemElement>() is not { } item)
                    return false;
                PlaceAt(item, BonusAt);
                Finish(item);
                ClearFilters();
                return true;
            },
            Leave = ClearFilters,
        });
        Add(Free("Put it in the way",
            "Feel free to move it, as long as the runner will run through it.\n\nClick Next when you're done.",
            () => Many(Scene(), Named("MoveTool"), Named("RotateTool"), Named("ScaleTool"), Named("RectTool"))));
        Add(Play("Collect it",
            "Play the level and grab the bonus.\n\nClose the game when you're done."));

        // ---------------- Trigger ----------------
        Section("Trigger");

        Add(Info("The camera stays behind",
            "Notice how the camera stayed put instead of following the runner?\n\nA trigger can fix that. It's a zone that does something when the runner touches it."));
        Add(Act("Triggers",
            "Click Trigger.",
            () => IsElement(ElementType.Trigger) && Editor?.PlacementType == ElementType.Trigger,
            () => Many(ElementButton(ElementType.Trigger))));
        Add(new TutorialStep
        {
            Title = "Place the trigger",
            Text = "Drag in the Scene to draw the trigger over the orange box.",
            Interactive = true,
            Targets = () => Many(Scene()),
            Guides = () => new[] { new SceneGuide(Box: TriggerBox) },
            Enter = () =>
            {
                Snapshot();
                Look(TriggerBox);
            },
            Done = () =>
            {
                if (Placed<TriggerElement>() is not { } trigger)
                    return false;
                _trigger = trigger;
                _triggerName = trigger.Name;
                Finish(trigger);
                return true;
            },
        });
        Add(new TutorialStep
        {
            Title = "Name it",
            Text = "Give the trigger a name in the Inspector. Anything works.\n\nPress Enter when you're done.",
            Interactive = true,
            Targets = () => Many(Inspector()),
            Tick = () => KeepSelected(_trigger),
            Done = () => _trigger != null && !string.IsNullOrWhiteSpace(_trigger.Name) && _trigger.Name != _triggerName,
        });
        Add(new TutorialStep
        {
            Title = "The Trigger Editor",
            Text = "What a trigger does is set up in the Trigger Editor.\n\nClick Open Trigger Editor.",
            Interactive = true,
            Targets = () => Many(WithText<Button>("Open Trigger Editor")),
            Tick = () => KeepSelected(_trigger),
            Done = () => Editor?.TriggerEditor != null,
        });

        Section("Trigger Editor");
        static Control? TriggerEditorView() => Find<Views.Editor.TriggerEditorView>();
        static Control? InTriggerEditor(string name) => TriggerEditorView()?.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == name);

        Add(Info("The top bar",
            "Apply saves your changes into the trigger.\n\nPresets and Edit Templates let you reuse ready-made triggers.",
            () => Many(TriggerEditorView()?.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("toolbar")))));
        Add(Info("Loops",
            "A trigger is made of loops.\n\nEach one waits for something to happen, checks its conditions, then does its actions.",
            () => Many(AncestorPanel(InTriggerEditor("LoopList")))));
        Add(Info("Variables",
            "Variables are values the trigger reads and changes.\n\nAI decides who can set it off.",
            () => Many(AncestorPanel(InTriggerEditor("VarsPanel")))));
        Add(Info("The selected loop",
            "This side edits the selected loop.\n\nWhen: what it waits for.\nOnly If: its conditions.\nDo: its actions.",
            () => Many(InTriggerEditor("MainScroll"))));

        TriggerEditorViewModel? TriggerVm() => Editor?.TriggerEditor;
        Add(new TutorialStep
        {
            Title = "Add a loop",
            Text = "Click New Loop.",
            Interactive = true,
            Targets = () => Many(TriggerEditorView() is { } v ? v.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => ReferenceEquals(b.Command, TriggerVm()?.AddLoopCommand)) : null),
            Enter = () => _count = TriggerVm()?.Data.loops.Count ?? 0,
            Done = () => TriggerVm() is { } t && t.Data.loops.Count > _count,
        });
        Add(Act("When",
            "Under When, click Add Event and choose Model Enters the Trigger.",
            () => TriggerVm() is { } t && t.Data.loops.Any(l => l.events.Any(e => e.id == "Enter")),
            () => Many(WithText<Button>("Add Event"))));
        Add(Act("Do",
            "Under Do, click Add Action and choose Camera › Follow Model.",
            () => TriggerVm() is { } t && t.Data.loops.Any(l => l.acts.Any(a => a.id == "Camera.Follow")),
            () => Many(WithText<Button>("Add Action"))));
        Add(Act("Only the runner",
            "Set AI to Player.\n\nNow only the runner sets it off, not the hunter.",
            () => TriggerVm() is { } t && t.Data.vars.Any(v => v.name == "$AI" && v.value == "0"),
            () => Many(InTriggerEditor("VarsPanel") is { } vars
                ? vars.GetVisualChildren().OfType<Grid>().FirstOrDefault(g => g.Children.OfType<TextBlock>().Any(tb => tb.Text == TriggerCatalog.VarLabel("$AI")))
                : null)));
        Add(Act("Apply",
            "Click Apply to save the trigger.",
            () => _trigger != null && _trigger.ContentXml.Contains("Follow", StringComparison.Ordinal) && TriggerVm() is { IsDirty: false },
            () => Many(TriggerEditorView() is { } v ? v.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => ReferenceEquals(b.Command, TriggerVm()?.ApplyCurrentCommand) && b.IsEffectivelyVisible) : null)));
        Add(Act("Close it",
            "Close the Trigger Editor with the X in the top right.",
            () => Editor is { TriggerEditor: null },
            () => Many(TriggerEditorView() is { } v ? v.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => ReferenceEquals(b.Command, TriggerVm()?.CloseEditorCommand)) : null)));
        Add(Play("Follow me",
            "Play the level. Once the runner reaches the trigger, the camera follows.\n\nClose the game when you're done."));

        // ---------------- Level Settings ----------------
        Section("Level Settings");

        Add(Act("Level Settings",
            "Almost there. Some settings apply to the whole level.\n\nClick Level Settings.",
            () => OpenDialog<LevelSettingsViewModel>() != null,
            () => Many(ByCommand(Editor?.OpenLevelSettingsCommand))));
        Add(Info("Level Settings",
            "This is where the level's thumbnail, music, coins and characters live.",
            null, dialog: true));
        Add(Interactive(Info("Music",
            "Pick your level's music here.\n\nThe play button lets you listen first.",
            () => Many(Find<ComboBox>(c => ReferenceEquals(c.ItemsSource, OpenDialog<LevelSettingsViewModel>()?.MusicChoices), dialog: true)?.Parent as Control),
            dialog: true)));
        Add(Act("Models",
            "Open the Models tab.",
            () => Find<TabItem>(t => t.Header as string == Loc.T("Models"), dialog: true)?.IsSelected == true,
            () => Many(Find<TabItem>(t => t.Header as string == Loc.T("Models"), dialog: true)),
            dialog: true));
        Add(Info("The characters",
            "These are the level's characters.\n\nName and Type say who it is, and Color tints it.\nBirth Spawn is the spawn it starts from.\nAI is who controls it: the player or the computer.\nTime is how long it waits before it starts running.\n\nSkins change how it looks.",
            () => Many(Find<TabControl>(dialog: true)),
            dialog: true));
        Add(Act("General",
            "Go back to the General tab.",
            () => Find<TabItem>(t => t.Header as string == Loc.T("General"), dialog: true)?.IsSelected == true,
            () => Many(Find<TabItem>(t => t.Header as string == Loc.T("General"), dialog: true)),
            dialog: true));
        Add(Act("A thumbnail",
            "Every level gets a thumbnail for the level list.\n\nClick Capture from Scene…",
            () => OpenDialog<ThumbnailCaptureViewModel>() != null,
            () => Many(ByCommand(OpenDialog<LevelSettingsViewModel>()?.CaptureThumbnailCommand, dialog: true)),
            dialog: true));
        Add(new TutorialStep
        {
            Title = "Capture",
            Text = "Pan and zoom until your level looks good in the frame.\n\nThen click Apply.",
            Interactive = true,
            Dimmed = false,
            InDialog = true,
            CardTopLeft = true,
            Done = () => OpenDialog<ThumbnailCaptureViewModel>() == null,
        });
        Add(Act("Apply",
            "Click Apply to close Level Settings.",
            () => OpenDialog<LevelSettingsViewModel>() == null,
            () => Many(ByCommand(OpenDialog<LevelSettingsViewModel>()?.ApplyCommand, "accent", dialog: true)),
            dialog: true));

        // ---------------- Save ----------------
        Section("Finishing Up");

        Add(new TutorialStep
        {
            Title = "Save",
            Text = "Last step: save your level.\n\nOpen the File menu and choose Save, or press Ctrl+S.",
            Interactive = true,
            Targets = () => Many(Find<MenuItem>(m => m.Header as string == Loc.T("_File"))),
            Enter = () => _signal = TutorialSignals.Saves,
            Done = () => TutorialSignals.Saves > _signal,
        });

        // ---------------- Debrief ----------------

        Add(new TutorialStep
        {
            Title = "You built a level!",
            Text = "That's it. You made a project, built a level with a spawn, camera, background, platform, obstacle, bonus and trigger, played it and saved it.\n\nFrom here, try other objects and textures, and use triggers to make things happen. Help › Keybinds lists every shortcut, and Help › Tutorial runs this again.\n\nHave fun building!",
            NextText = "Finish",
        });

        return steps;
    }
}
