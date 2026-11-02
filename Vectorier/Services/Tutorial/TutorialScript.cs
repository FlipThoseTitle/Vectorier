using System;
using System.Collections.Generic;
using System.Linq;
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
    private const double PlatformWidth = 2000;
    private const double PlatformHeight = 500;

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
            "Everything you make lives in a project: its levels, textures, objects and settings, kept apart from the game's own files. You can have as many projects as you like, and this window is where you pick one.",
            () => Many(Find<ListBox>(l => l.Name == "ProjectListBox"))));

        Add(new TutorialStep
        {
            Title = "Create a project",
            Text = "Let's make your first project. Click the + button (Add Project) and give it any name you like.",
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
            DynamicText = () => Loc.F("Here it is. Click {0} in the list to select it.", _projectName ?? string.Empty),
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
            "On the right are the project's details. Click the picture to give the project a thumbnail, and change its name, author or description whenever you like.",
            () => Many(Named("DetailsPanel"))));

        Add(Act("Open the project",
            "Click Open Selected Project to start working on it.",
            () => Manager != null,
            () => Many(ByCommand(Selection?.OpenProjectCommand))));

        // ---------------- Project Manager ----------------
        Section("The Project Manager");

        Add(Info("The Project Manager",
            "This is the Project Manager: everything in your project, sorted into categories. Each one has its own tab on the left. Let's go through them.",
            () => Many(Find<ListBox>(l => l.Classes.Contains("nav")))));

        Add(new TutorialStep
        {
            Title = "Levels",
            Text = "Levels are the stages you play, grouped by location and by mode (Story or Hunter). Click DOWNTOWN_STORY_01 in the list.",
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
            "The right side shows the selected level: its thumbnail, the name shown in the game, its location and its track settings. You can change them here at any time.",
            () => Many(AncestorPanel(ByCommand(Manager?.Levels.OpenInEditorCommand, "accent")))));

        Add(Info("Edit Level",
            "Edit Level opens the selected level in the editor. We will use it in a moment.",
            () => Many(ByCommand(Manager?.Levels.OpenInEditorCommand, "accent"))));

        Add(Info("The Editor button",
            "The Editor button at the bottom opens the level editor with a new, empty level, without picking one here first.",
            () => Many(ByCommand(Manager?.OpenEmptyEditorCommand))));

        // The other categories: click, then a short explanation.
        Section("Categories");
        var categories = new (int Index, string Text)[]
        {
            (1, "Locations group levels together, like Downtown or Construction. Each has its own levels and settings, and you can add or reorder them here."),
            (2, "Textures are the pictures your levels are made of. Browse the project's textures here, or import your own."),
            (3, "Animations holds the moves file and the animation files of the characters' moves. Select one to preview it on the character."),
            (4, "Tricks are the special moves players buy in the shop and perform in levels. Here you set their names, icons and prices."),
            (5, "Gears are the items players buy in the shop. Here too you set their names, icons and prices."),
            (6, "Models are the files of the game's characters, such as the runner and the hunter."),
            (7, "Music is the songs your levels can play."),
            (8, "Sounds are the game's sound effects."),
            (9, "Videos are the cutscenes that can play at the start or the end of a level."),
            (10, "XML Files are the project's data files. Vectorier writes them for you, so you rarely need to open them yourself."),
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
            "That's every category. Now click Levels to go back.",
            () => Manager is { } m && ReferenceEquals(m.SelectedPage, m.Levels),
            () => Many(ItemOf(Manager?.Levels))));

        Add(new TutorialStep
        {
            Title = "Create a level",
            Text = "Time to make a level of your own. Click New Level (the + button) to add one to Downtown.",
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
            DynamicText = () => Loc.F("Your new level {0} is selected. Click Edit Level to open it in the editor.", Manager?.Levels.SelectedItem?.Name ?? string.Empty),
            Interactive = true,
            Targets = () => Many(ByCommand(Manager?.Levels.OpenInEditorCommand, "accent")),
            Done = () => Editor != null,
        });

        // ---------------- Editor tour ----------------
        Section("The Level Editor");

        Add(Info("The Level Editor",
            "Welcome to the Level Editor, where you build levels. Let's have a quick look around first."));
        Add(Info("The top menu",
            "The top menu has every command: File (save, play, Level Settings), Edit (undo, copy, delete...), Element (add things to the level) and Help.",
            () => Many(Find<Menu>())));
        Add(Info("The toolbar",
            "The toolbar holds the tools that move, rotate and resize things, plus Undo, Save, Level Settings and the way back to the Project Manager.",
            () => Many(Find<Border>(b => b.Classes.Contains("toolbar") && b.FindAncestorOfType<Views.Editor.EditorView>() != null && b.FindAncestorOfType<Views.Editor.TriggerEditorView>() == null))));
        Add(Info("The Element Bar",
            "The Element Bar lists everything you can put in a level: images, platforms, objects, triggers, spawns and more. Click one to start placing it. The eye next to it shows or hides that kind in the Scene.",
            () => Many(Named("ElementBar"))));
        Add(Info("The Hierarchy",
            "The Hierarchy lists everything in the level as a tree. Click a name to select it.",
            () => Many(Hierarchy())));
        Add(Info("The Project window",
            "The Project window shows what you can place for the chosen element: textures for images, objects, platforms and so on.",
            () => Many(ProjectWindow())));
        Add(Info("The Inspector",
            "The Inspector shows the settings of the selected element: its position, size, layer and more.",
            () => Many(Inspector())));
        // Try it: the Scene takes the mouse here (Next when done).
        Add(Interactive(Info("The Scene",
            "And in the middle is the Scene: your level. Try it: drag with the right mouse button to look around, and use the mouse wheel to zoom. Click Next when you're done.",
            () => Many(Scene()))));

        // ---------------- Spawn ----------------
        Section("Spawn");

        Add(Act("Add a spawn",
            "Every level needs a place where the runner starts. Click Spawn in the Element Bar.",
            () => IsElement(ElementType.Spawn) && Editor?.PlacementType == ElementType.Spawn,
            () => Many(ElementButton(ElementType.Spawn))));
        Add(new TutorialStep
        {
            Title = "Place the spawn",
            Text = "Now click anywhere in the Scene to place it.",
            Interactive = true,
            Targets = () => Many(Scene()),
            Enter = Snapshot,
            Done = () =>
            {
                if (Placed<SpawnElement>() is not { } spawn)
                    return false;
                _spawn = spawn;
                Finish(spawn);
                return true;
            },
        });
        Add(Info("The spawn point",
            "This is the spawn point: where the runner appears when the level starts.",
            () => Many(Scene())));
        Add(new TutorialStep
        {
            Title = "Select the spawn",
            Text = "Click the spawn in the Scene (or its name in the Hierarchy) to select it.",
            Interactive = true,
            Targets = () => Many(Scene(), Hierarchy()),
            Enter = () => Editor?.ClearSelection(),
            Done = () => Editor is { } e && _spawn != null && e.Selection.Contains(_spawn),
        });
        Add(new TutorialStep
        {
            Title = "Preview Animation",
            Text = "With a spawn selected, the Inspector has a Preview Animation button. Click it.",
            Interactive = true,
            Targets = () => Many(WithText<Button>("Preview Animation")),
            Tick = () => KeepSelected(_spawn),
            Done = () => Editor is { } e && e.IsMoveVisualizerTab && e.MoveVisualizer.IsPlaced,
        });
        Add(Info("The runner",
            "The runner appears at the spawn and plays its starting move, so you can see where it will go. The Move Visualizer on the right can play any move anywhere in the level, to check jumps and distances while you build.",
            () => Many(Scene(), Find<Views.Editor.MoveVisualizerView>())));
        Add(new TutorialStep
        {
            Title = "Back to the Inspector",
            Text = "Click the Inspector tab to go back.",
            Interactive = true,
            Targets = () => Many(WithText<RadioButton>("Inspector")),
            Done = () => Editor?.IsInspectorTab == true,
            Leave = () => Editor?.MoveVisualizer.ClearCommand.Execute(null),
        });

        // ---------------- Camera ----------------
        Section("Camera");

        Add(Act("Add a camera",
            "Next, the camera. Click Camera in the Element Bar.",
            () => IsElement(ElementType.Camera) && Editor?.PlacementType == ElementType.Camera,
            () => Many(ElementButton(ElementType.Camera))));
        Add(new TutorialStep
        {
            Title = "Place the camera",
            Text = "Click in the Scene, near the spawn, to place the camera.",
            Interactive = true,
            Targets = () => Many(Scene()),
            Enter = Snapshot,
            Done = () =>
            {
                if (Placed<CameraElement>() is not { } camera)
                    return false;
                _camera = camera;
                Finish(camera);
                return true;
            },
        });
        Add(new TutorialStep
        {
            Title = "The Game view",
            Text = "The camera decides what the player sees at the start. Click the Game tab to look through it.",
            Interactive = true,
            Targets = () => Many(WithText<RadioButton>("Game")),
            Tick = () => KeepSelected(_camera),
            Done = () => Editor?.IsGameViewTab == true,
        });
        Add(Info("The Game view",
            "This is what the camera sees, like the game would show it. The Game view is only for looking: you build in the Scene.",
            () => Many(GameScene())));
        Add(Act("Back to the Scene",
            "Click the Scene tab to go back.",
            () => Editor?.IsSceneViewTab == true,
            () => Many(WithText<RadioButton>("Scene"))));

        // ---------------- Background ----------------
        Section("Background");

        Add(Info("A background",
            "Let's give the level a background."));
        Add(new TutorialStep
        {
            Title = "Images",
            Text = "Images are pictures placed in the level. Click Images in the Element Bar.",
            Interactive = true,
            Targets = () => Many(ElementButton(ElementType.Image)),
            Enter = () => Editor?.ProjectWindow.SetTutorialFilter(
                f => Editor!.ProjectWindow.FolderHoldsTexture(f, BackgroundTexture),
                i => string.Equals(i.Texture?.Name, BackgroundTexture, StringComparison.OrdinalIgnoreCase)),
            Done = () => IsElement(ElementType.Image),
        });
        Add(Act("Pick the background",
            "The Project window now shows the textures (we hid all but one for now). Click v_bg.",
            () => Editor is { } e && string.Equals(e.ProjectWindow.SelectedItem?.Texture?.Name, BackgroundTexture, StringComparison.OrdinalIgnoreCase) && e.PlacementType == ElementType.Image,
            () => Many(ProjectWindow())));
        Add(new TutorialStep
        {
            Title = "Place the background",
            Text = "Click in the Scene to place it. The see-through picture under the pointer shows where it will go.",
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
            Text = "Something far away should move slower than the ground when the runner runs past. Layers do that. In the Inspector, set Layer to Background.",
            Interactive = true,
            Targets = () => Many(Find<ComboBox>(c => ReferenceEquals(c.ItemsSource, InspectorViewModel.LayerNames))),
            Tick = () => KeepSelected(_background),
            Done = () => _background?.Layer == "Background",
            Leave = () =>
            {
                // Wherever it was placed, it goes where it covers the start of the level.
                if (_background != null)
                {
                    _background.X = -1000;
                    _background.Y = -500;
                }
            },
        });
        Add(Info("The background is set",
            "The background is now on the Background layer, so it scrolls slowly in the game. We also moved it to -1000, 500, so it sits behind the start of the level.",
            () => Many(Scene(), Inspector())));

        // ---------------- Platform ----------------
        Section("Platform");

        Add(Info("A platform",
            "The runner has nowhere to stand! Let's add a platform: the ground it runs on."));
        Add(Act("Platforms",
            "Click Platform in the Element Bar.",
            () => IsElement(ElementType.Platform),
            () => Many(ElementButton(ElementType.Platform))));
        Add(Act("Pick a style",
            "The Project window shows three platform styles: rough, smooth and block. Click rough.",
            () => Editor is { } e && e.ProjectWindow.SelectedItem?.PlatformSurface == PlatformSurfaceType.Rough && e.PlacementType == ElementType.Platform,
            () => Many(ProjectWindow())));
        Add(new TutorialStep
        {
            Title = "Place the platform",
            Text = "Click in the Scene to place it, or drag to draw its size.",
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
        Tool("The Move tool", "Tools change what dragging does. Pick the Move tool in the toolbar (or press Q).", EditorTool.Move, "MoveTool");
        Drag("Move it", "Drag the platform, or one of its arrows, to move it. Let go when you're done.", () => TutorialSignals.MoveDrags);
        Tool("The Scale tool", "Now pick the Scale tool (or press E).", EditorTool.Scale, "ScaleTool");
        Drag("Scale it", "Drag one of the handles to stretch the platform.", () => TutorialSignals.ScaleDrags);
        Tool("The Rect tool", "Last one: pick the Rect tool (or press T).", EditorTool.Rect, "RectTool");
        Drag("Resize it", "Drag an edge or a corner to resize the platform.", () => TutorialSignals.RectDrags);

        Add(new TutorialStep
        {
            Title = "Make room to run",
            Text = "The runner needs room. Make the platform 2000 wide and 500 high: drag its edges (it stops growing at that size), or type the size in the Inspector.",
            Interactive = true,
            Targets = () => Many(Scene(), Inspector()),
            Tick = () =>
            {
                KeepSelected(_platform);
                if (_platform == null)
                    return;
                if (_platform.Width > PlatformWidth)
                    _platform.Width = PlatformWidth;
                if (_platform.Height > PlatformHeight)
                    _platform.Height = PlatformHeight;
            },
            Done = () => _platform != null && _platform.Width >= PlatformWidth - 0.5 && _platform.Height >= PlatformHeight - 0.5,
        });

        // ---------------- Play ----------------

        Add(Play("Try it!",
            "Your level can be played now. You can still move things around in the Scene: put the spawn just above the platform so the runner lands on it. When you're ready, click Play, and close the game when you're done."));

        // ---------------- Obstacle ----------------
        Section("Obstacle");

        Add(Info("An obstacle",
            "Running is nice, but let's add something to jump over."));
        Add(new TutorialStep
        {
            Title = "Objects",
            Text = "Objects are ready-made pieces: obstacles, buildings, props. Click Objects in the Element Bar.",
            Interactive = true,
            Targets = () => Many(ElementButton(ElementType.Object)),
            Enter = () => Editor?.ProjectWindow.SetTutorialFilter(
                f => string.Equals(f.FileName, ObstacleFile, StringComparison.OrdinalIgnoreCase),
                i => i.Label == ObstacleName),
            Done = () => IsElement(ElementType.Object),
        });
        Add(Act("Pick an obstacle",
            "Objects are stored in XML files, listed on the left. On the right are the objects of the chosen file. Click SmallPipeT.",
            () => Editor is { } e && e.ProjectWindow.SelectedItem?.Label == ObstacleName && e.PlacementType == ElementType.Object,
            () => Many(ProjectWindow())));
        Add(new TutorialStep
        {
            Title = "Place the obstacle",
            Text = "Click in the Scene to place it, somewhere above the platform.",
            Interactive = true,
            Targets = () => Many(Scene()),
            Enter = Snapshot,
            Done = () =>
            {
                if (Placed<ObjectElement>(o => o.Name == ObstacleName || o.Reference?.Name == ObstacleName) is not { } obstacle)
                    return false;
                _obstacle = obstacle;
                Finish(obstacle);
                ClearFilters();
                return true;
            },
            Leave = ClearFilters,
        });
        Add(new TutorialStep
        {
            Title = "Snapping",
            Text = "Snapping puts things exactly on a platform. Point at the obstacle in the Scene and hold C: its corners light up. Grab its bottom corner and drag it onto the top of the platform, then let go.",
            Interactive = true,
            Targets = () => Many(Scene()),
            Enter = () => _signal = TutorialSignals.PlatformSnapDrags,
            Done = () => TutorialSignals.PlatformSnapDrags > _signal,
        });
        Add(Free("Your turn",
            "Move the obstacle wherever you like on the platform. Click Next when you're happy with it.",
            () => Many(Scene(), Named("MoveTool"), Named("RotateTool"), Named("ScaleTool"), Named("RectTool"))));
        Add(Play("Jump over it",
            "Play the level and jump over the obstacle with the Up key. Close the game when you're done."));

        // ---------------- Item ----------------
        Section("Item");

        Add(new TutorialStep
        {
            Title = "Items",
            Text = "Items are things the runner collects. Click Item in the Element Bar.",
            Interactive = true,
            Targets = () => Many(ElementButton(ElementType.Item)),
            Enter = () => Editor?.ProjectWindow.SetTutorialFilter(null, i => i.Label == "Bonus"),
            Done = () => IsElement(ElementType.Item),
        });
        Add(Act("Pick a bonus",
            "Click Bonus in the Project window.",
            () => Editor is { } e && e.ProjectWindow.SelectedItem?.Label == "Bonus" && e.PlacementType == ElementType.Item,
            () => Many(ProjectWindow())));
        Add(new TutorialStep
        {
            Title = "Place the bonus",
            Text = "Click in the Scene to place it.",
            Interactive = true,
            Targets = () => Many(Scene()),
            Enter = Snapshot,
            Done = () =>
            {
                if (Placed<ItemElement>() is not { } item)
                    return false;
                Finish(item);
                ClearFilters();
                return true;
            },
            Leave = ClearFilters,
        });
        Add(Free("Put it in the way",
            "Move the bonus anywhere the runner will pass, for example just above the obstacle. Click Next when you're done.",
            () => Many(Scene(), Named("MoveTool"), Named("RotateTool"), Named("ScaleTool"), Named("RectTool"))));
        Add(Play("Collect it",
            "Play the level and grab the bonus. Close the game when you're done."));

        // ---------------- Trigger ----------------
        Section("Trigger");

        Add(Info("The camera stays behind",
            "Did you notice? The camera stays where you put it instead of following the runner. A trigger can fix that: a zone that does something when the runner touches it."));
        Add(Act("Triggers",
            "Click Trigger in the Element Bar.",
            () => IsElement(ElementType.Trigger) && Editor?.PlacementType == ElementType.Trigger,
            () => Many(ElementButton(ElementType.Trigger))));
        Add(new TutorialStep
        {
            Title = "Place the trigger",
            Text = "Drag in the Scene to draw the trigger where the runner passes, for example over the spawn.",
            Interactive = true,
            Targets = () => Many(Scene()),
            Enter = Snapshot,
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
            Text = "Give the trigger a name in the Inspector (any name will do), then press Enter.",
            Interactive = true,
            Targets = () => Many(Inspector()),
            Tick = () => KeepSelected(_trigger),
            Done = () => _trigger != null && !string.IsNullOrWhiteSpace(_trigger.Name) && _trigger.Name != _triggerName,
        });
        Add(new TutorialStep
        {
            Title = "The Trigger Editor",
            Text = "What a trigger does is set in the Trigger Editor. Click Open Trigger Editor.",
            Interactive = true,
            Targets = () => Many(WithText<Button>("Open Trigger Editor")),
            Tick = () => KeepSelected(_trigger),
            Done = () => Editor?.TriggerEditor != null,
        });

        Section("Trigger Editor");
        static Control? TriggerEditorView() => Find<Views.Editor.TriggerEditorView>();
        static Control? InTriggerEditor(string name) => TriggerEditorView()?.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == name);

        Add(Info("The top bar",
            "At the top: Apply writes your changes into the trigger. Presets and Edit Templates let you reuse ready-made triggers.",
            () => Many(TriggerEditorView()?.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("toolbar")))));
        Add(Info("Loops",
            "A trigger is made of loops. Each loop waits for something to happen, checks its conditions, then does its actions.",
            () => Many(AncestorPanel(InTriggerEditor("LoopList")))));
        Add(Info("Variables",
            "Variables are values the trigger reads and changes. AI decides who can set it off: anyone, only the player or only the hunter.",
            () => Many(AncestorPanel(InTriggerEditor("VarsPanel")))));
        Add(Info("The selected loop",
            "The right side edits the selected loop: When (the events it waits for), Only If (its conditions) and Do (its actions).",
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
            "In Variables, set AI to Player, so only the runner sets it off (and not the hunter).",
            () => TriggerVm() is { } t && t.Data.vars.Any(v => v.name == "$AI" && v.value == "0"),
            () => Many(InTriggerEditor("VarsPanel") is { } vars
                ? vars.GetVisualChildren().OfType<Grid>().FirstOrDefault(g => g.Children.OfType<TextBlock>().Any(tb => tb.Text == TriggerCatalog.VarLabel("$AI")))
                : null)));
        Add(Act("Apply",
            "Click Apply to save the trigger.",
            () => _trigger != null && _trigger.ContentXml.Contains("Follow", StringComparison.Ordinal) && TriggerVm() is { IsDirty: false },
            () => Many(TriggerEditorView() is { } v ? v.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => ReferenceEquals(b.Command, TriggerVm()?.ApplyCurrentCommand) && b.IsEffectivelyVisible) : null)));
        Add(Act("Close it",
            "Close the Trigger Editor with the X at the top right.",
            () => Editor is { TriggerEditor: null },
            () => Many(TriggerEditorView() is { } v ? v.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => ReferenceEquals(b.Command, TriggerVm()?.CloseEditorCommand)) : null)));
        Add(Play("Follow me",
            "Play the level: once the runner runs into the trigger, the camera follows it. Close the game when you're done."));

        // ---------------- Level Settings ----------------
        Section("Level Settings");

        Add(Act("Level Settings",
            "Last, a few settings of the whole level. Click Level Settings in the toolbar.",
            () => OpenDialog<LevelSettingsViewModel>() != null,
            () => Many(ByCommand(Editor?.OpenLevelSettingsCommand))));
        Add(Info("Level Settings",
            "Level Settings holds what belongs to the whole level: its thumbnail, music, coins and characters.",
            null, dialog: true));
        Add(Interactive(Info("Music",
            "Pick the music your level plays here, and listen to it with the play button. Change it whenever you like.",
            () => Many(Find<ComboBox>(c => ReferenceEquals(c.ItemsSource, OpenDialog<LevelSettingsViewModel>()?.MusicChoices), dialog: true)?.Parent as Control),
            dialog: true)));
        Add(Act("Models",
            "Open the Models tab.",
            () => Find<TabItem>(t => t.Header as string == Loc.T("Models"), dialog: true)?.IsSelected == true,
            () => Many(Find<TabItem>(t => t.Header as string == Loc.T("Models"), dialog: true)),
            dialog: true));
        Add(Info("The characters",
            "These are the characters of the level. Each has a Name, a Type, a Color, a Birth Spawn (the spawn it starts at), an AI (who controls it: the player or the computer) and a Time (how long it waits before it starts running). Skins change how it looks.",
            () => Many(Find<TabControl>(dialog: true)),
            dialog: true));
        Add(Act("General",
            "Go back to the General tab.",
            () => Find<TabItem>(t => t.Header as string == Loc.T("General"), dialog: true)?.IsSelected == true,
            () => Many(Find<TabItem>(t => t.Header as string == Loc.T("General"), dialog: true)),
            dialog: true));
        Add(Act("A thumbnail",
            "Every level has a thumbnail, shown when picking levels. Click Capture from Scene…",
            () => OpenDialog<ThumbnailCaptureViewModel>() != null,
            () => Many(ByCommand(OpenDialog<LevelSettingsViewModel>()?.CaptureThumbnailCommand, dialog: true)),
            dialog: true));
        Add(new TutorialStep
        {
            Title = "Capture",
            Text = "Move and zoom the view to frame your level nicely, then click Apply.",
            Interactive = true,
            Dimmed = false,
            InDialog = true,
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
            Text = "Finally, save your level: open the File menu and choose Save (or press Ctrl+S).",
            Interactive = true,
            Targets = () => Many(Find<MenuItem>(m => m.Header as string == Loc.T("_File"))),
            Enter = () => _signal = TutorialSignals.Saves,
            Done = () => TutorialSignals.Saves > _signal,
        });

        // ---------------- Debrief ----------------

        Add(new TutorialStep
        {
            Title = "You built a level!",
            Text = "Well done! You created a project and a level with a spawn, a camera, a background, a platform, an obstacle, a bonus and a trigger, played it, and saved it.\n\nFrom here, try other objects and textures, use triggers to make things happen, and check your jumps with the Move Visualizer. Help › Keybinds lists every shortcut, and Help › Tutorial plays this tutorial again.\n\nHave fun building!",
            NextText = "Finish",
        });

        return steps;
    }
}
