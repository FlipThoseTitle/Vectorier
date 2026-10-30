using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Models.Scene;
using Vectorier.Services;
using Vectorier.Services.Projects;
using Vectorier.Services.Localization;

namespace Vectorier.ViewModels.Editor;

/// <summary>What a new element is created from (texture, prefab, stored trigger...).</summary>
public sealed record ElementSource(
    string? TextureName = null,
    ObjectDefinition? Definition = null,
    string? ModelName = null,
    ItemType? ItemType = null,
    StoredTrigger? Trigger = null,
    StoredArea? Area = null,
    Services.Assets.PlatformSurfaceType? PlatformSurface = null);

public sealed partial class EditorViewModel
{
    /// <summary>Level-space point at the center of the Scene View (set by the Scene control).</summary>
    public Point SceneViewCenter { get; set; }

    // ================= CREATION ================= //

    /// <summary>Element menu / Hierarchy "Create New": creates at the view center, inside <paramref name="parent"/>.</summary>
    [RelayCommand]
    private Task CreateFromMenu(ElementType type) => CreateFromMenuAsync(type, null, null);

    /// <summary>Element ▸ Item ▸ Bonus / Coin.</summary>
    [RelayCommand]
    private Task CreateItem(ItemType type) => CreateFromMenuAsync(ElementType.Item, null, type);

    public async Task CreateFromMenuAsync(ElementType type, SceneElement? context, ItemType? itemType, Point? levelPosition = null)
    {
        ObjectElement? parent = context as ObjectElement;
        int index = -1;
        if (context != null && parent == null)
        {
            // Only Objects hold children: create next to the clicked element instead.
            parent = context.Parent;
            index = Document.SiblingsOf(context).IndexOf(context) + 1;
        }

        ElementSource? source = null;
        switch (type)
        {
            case ElementType.Image:
            case ElementType.Animation:
                if (ProjectWindow.SelectedTextureName is not { } texture)
                {
                    await AppServices.Dialogs.ShowMessageAsync(Loc.F("Create {0}", type), Loc.T("Select an image in the Project Window first."), MessageKind.Warning);
                    return;
                }
                source = new ElementSource(TextureName: texture);
                break;
            case ElementType.Model:
                if (ProjectWindow.SelectedModelName is not { } model)
                {
                    await AppServices.Dialogs.ShowMessageAsync(Loc.T("Create Model"), Loc.T("Select a model in the Project Window first."), MessageKind.Warning);
                    return;
                }
                source = new ElementSource(ModelName: model);
                break;
            case ElementType.Item:
                source = new ElementSource(ItemType: itemType ?? ItemType.Bonus);
                break;
        }

        var element = await CreateElementAsync(type, levelPosition, null, source, parent, index, menuNaming: true);
        if (element != null)
            Select(element);
    }

    /// <summary>
    /// Creates an element. <paramref name="levelPosition"/> is a level-space point (Default layer); null uses the
    /// view center. <paramref name="size"/> sets the size of rectangle-like elements.
    /// </summary>
    public async Task<SceneElement?> CreateElementAsync(ElementType type, Point? levelPosition, Size? size, ElementSource? source,
        ObjectElement? parent = null, int index = -1, bool menuNaming = false)
    {
        var pos = levelPosition ?? SceneViewCenter;
        SceneElement? element;

        switch (type)
        {
            case ElementType.Object:
                if (IsPlacementDenied(source, out string denied))
                {
                    StatusText = denied;
                    await AppServices.Dialogs.ShowMessageAsync(Loc.T("Edit Object"), denied, MessageKind.Warning);
                    return null;
                }
                if (source?.Definition is { } def)
                {
                    BuildDefinition(def);
                    var center = def.Bounds is { } b ? b.Center : new Point(0, 0);
                    element = new ObjectElement { Name = def.Name, Reference = def, X = Math.Round(pos.X - center.X), Y = Math.Round(pos.Y - center.Y) };
                }
                else
                {
                    element = new ObjectElement { X = Math.Round(pos.X), Y = Math.Round(pos.Y) };
                }
                break;

            case ElementType.Image:
            case ElementType.Animation:
            case ElementType.Particle:
            {
                string texture = source?.TextureName ?? ParticleElement.DefaultClassName;
                var asset = await Context.Textures.GetAsync(texture);
                double w = asset?.Width ?? 100, h = asset?.Height ?? 100;
                SceneElement visual = type switch
                {
                    ElementType.Image => new ImageElement { NativeWidth = (int)w, NativeHeight = (int)h },
                    ElementType.Animation => new AnimationElement(),
                    _ => new ParticleElement(),
                };
                visual.Name = texture;
                visual.Width = w;
                visual.Height = h;
                visual.X = Math.Round(pos.X - w / 2);
                visual.Y = Math.Round(pos.Y - h / 2);
                element = visual;
                break;
            }

            case ElementType.Trigger:
            {
                var stored = source?.Trigger;
                var trigger = new TriggerElement
                {
                    Name = stored?.Name ?? "Trigger",
                    ContentXml = stored?.Content ?? string.Empty,
                    Width = size?.Width ?? stored?.Width ?? 100,
                    Height = size?.Height ?? stored?.Height ?? 100,
                };
                element = PlaceRect(trigger, pos, size);
                break;
            }

            case ElementType.Area:
            {
                var stored = source?.Area;
                var area = new AreaElement
                {
                    Name = stored?.Name ?? "Area",
                    Width = size?.Width ?? stored?.Width ?? 100,
                    Height = size?.Height ?? stored?.Height ?? 100,
                };
                if (stored != null)
                {
                    area.AreaType = stored.AreaType;
                    area.Distance = stored.Distance;
                    area.ItemName = stored.ItemName;
                    area.Score = stored.Score;
                    area.Key = stored.Key;
                    area.Description = stored.Description;
                }
                if (area.IsTrick)
                {
                    await EnsureTrickTriggersAsync();
                    area.Name = TrickAreaName(area.ItemName);
                }
                element = PlaceRect(area, pos, size);
                break;
            }

            case ElementType.Platform when source?.PlatformSurface is { } surface:
            {
                // Rough / Smooth / Block: the Platform dressed with block textures, 500 × 500 unless dragged.
                var rect = size is { } sz
                    ? new Rect(Math.Round(pos.X), Math.Round(pos.Y), Math.Round(sz.Width), Math.Round(sz.Height))
                    : new Rect(Math.Round(pos.X - 250), Math.Round(pos.Y - 250), 500, 500);
                element = await Services.Assets.PlatformSurface.BuildAsync(Context.Textures, surface, rect, Random.Shared.Next(1, int.MaxValue));
                element.Name = string.Empty;
                break;
            }

            case ElementType.Platform:
                element = PlaceRect(new PlatformElement { Width = size?.Width ?? 100, Height = size?.Height ?? 100 }, pos, size);
                element.ApplyIntegerLock();
                break;

            case ElementType.Trapezoid:
            {
                var trapezoid = new TrapezoidElement { TrapezoidType = 1, Width = Math.Max(2, Math.Round(size?.Width ?? 200)), BaseHeight = 20 };
                trapezoid.ApplySlopeAngle();
                trapezoid.X = Math.Round(pos.X - (size == null ? trapezoid.Width / 2 : 0));
                trapezoid.Y = Math.Round(pos.Y - (size == null ? trapezoid.Height / 2 : 0));
                element = trapezoid;
                break;
            }

            case ElementType.Camera:
            {
                var cameras = Document.AllElements().OfType<CameraElement>().ToList();
                if (cameras.Count >= 2)
                {
                    await AppServices.Dialogs.ShowMessageAsync(Loc.T("Create Camera"),
                        Loc.T("This level already has a Camera for Common Mode and one for Hunter Mode. Delete one of them first."), MessageKind.Warning);
                    return null;
                }
                var camera = new CameraElement { Name = "Camera", X = Math.Round(pos.X), Y = Math.Round(pos.Y) };
                if (cameras.Count == 1)
                {
                    var existing = cameras[0];
                    if (existing.Selection == ModeSelection.All)
                    {
                        existing.Selection = ModeSelection.CommonMode;
                        StatusText = Loc.T("The existing Camera now applies to Common Mode only; the new one to Hunter Mode.");
                    }
                    camera.Selection = existing.Selection == ModeSelection.HunterMode ? ModeSelection.CommonMode : ModeSelection.HunterMode;
                }
                element = camera;
                break;
            }

            case ElementType.Spawn:
            {
                var spawn = new SpawnElement { X = Math.Round(pos.X), Y = Math.Round(pos.Y) };
                element = spawn;
                break;
            }

            case ElementType.Model:
            {
                string? model = source?.ModelName;
                if (string.IsNullOrEmpty(model))
                {
                    await AppServices.Dialogs.ShowMessageAsync(Loc.T("Place Model"), Loc.T("Select a model in the Project Window first."), MessageKind.Error);
                    return null;
                }
                element = new ModelElement { Name = model, X = Math.Round(pos.X), Y = Math.Round(pos.Y) };
                break;
            }

            case ElementType.Item:
            {
                var item = new ItemElement { X = Math.Round(pos.X), Y = Math.Round(pos.Y) };
                item.ItemType = source?.ItemType ?? Models.Scene.ItemType.Bonus;
                item.Name = item.ItemType == Models.Scene.ItemType.Bonus ? "Bonus" : "Coin";
                element = item;
                break;
            }

            case ElementType.Comment:
                element = PlaceRect(new CommentElement { Name = "Comment", Width = size?.Width ?? 300, Height = size?.Height ?? 150 }, pos, size);
                break;

            case ElementType.BuildingIn:
            case ElementType.BuildingOut:
            {
                // One In and one Out per Building: placing again moves the existing marker.
                bool isIn = type == ElementType.BuildingIn;
                if (!IsEditingBuilding)
                    return null;
                var existing = Document.Roots.OfType<BuildingMarkerElement>().FirstOrDefault(m => m.IsIn == isIn);
                if (existing != null)
                {
                    existing.X = Math.Round(pos.X);
                    existing.Y = Math.Round(pos.Y);
                    MarkChanged();
                    SceneInvalidated?.Invoke();
                    return existing;
                }
                element = new BuildingMarkerElement(isIn) { X = Math.Round(pos.X), Y = Math.Round(pos.Y) };
                parent = null;
                break;
            }

            default:
                return null;
        }

        // Objects with a definition never get children: the element goes next to them instead.
        (parent, index) = HostFor(parent, index);

        // Convert the level position into the parent's local space.
        if (parent != null)
        {
            var origin = parent.WorldPosition;
            element.X -= origin.X;
            element.Y -= origin.Y;
            element.ApplyIntegerLock();
        }

        Document.Insert(element, parent, index);

        if (element is SpawnElement newSpawn)
            newSpawn.Name = UniqueSpawnName(newSpawn, SpawnElement.DefaultName, menuNaming);

        RaiseStructureChanged();
        MarkChanged();
        RevealInHierarchy(element);
        return element;
    }

    /// <summary>
    /// The element a click would create at <paramref name="pos"/>, for the semi-transparent placement preview.
    /// Built like <see cref="CreateElementAsync"/> but without side effects; null when it can't be shown yet.
    /// </summary>
    public SceneElement? BuildPlacementGhost(ElementType type, Point pos, ElementSource? source)
    {
        switch (type)
        {
            case ElementType.Object:
                if (source?.Definition is not { } def)
                    return new ObjectElement { X = Math.Round(pos.X), Y = Math.Round(pos.Y) };
                BuildDefinition(def);
                var center = def.Bounds is { } b ? b.Center : new Point(0, 0);
                return new ObjectElement { Name = def.Name, Reference = def, X = Math.Round(pos.X - center.X), Y = Math.Round(pos.Y - center.Y) };

            case ElementType.Image:
            case ElementType.Animation:
            case ElementType.Particle:
            {
                string texture = source?.TextureName ?? ParticleElement.DefaultClassName;
                var asset = Context.Textures.TryGet(texture);
                if (asset == null)
                    return null; // loading: shown once it is ready
                double w = asset.Width, h = asset.Height;
                SceneElement visual = type switch
                {
                    ElementType.Image => new ImageElement { NativeWidth = (int)w, NativeHeight = (int)h },
                    ElementType.Animation => new AnimationElement(),
                    _ => new ParticleElement(),
                };
                visual.Name = texture;
                visual.Width = w;
                visual.Height = h;
                visual.X = Math.Round(pos.X - w / 2);
                visual.Y = Math.Round(pos.Y - h / 2);
                if (visual is ImageElement image) image.AutoSize = false;
                return visual;
            }

            case ElementType.Trigger:
                return PlaceRect(new TriggerElement { Name = source?.Trigger?.Name ?? "Trigger", Width = source?.Trigger?.Width ?? 100, Height = source?.Trigger?.Height ?? 100 }, pos, null);
            case ElementType.Area:
                return PlaceRect(new AreaElement { Name = source?.Area?.Name ?? "Area", AreaType = source?.Area?.AreaType ?? "Animation", Width = source?.Area?.Width ?? 100, Height = source?.Area?.Height ?? 100 }, pos, null);
            case ElementType.Platform when source is { PlatformSurface: not null, Definition: { } preview }:
                return new ObjectElement { Reference = preview, X = Math.Round(pos.X - 250), Y = Math.Round(pos.Y - 250) };
            case ElementType.Platform:
            {
                var platform = PlaceRect(new PlatformElement { Width = 100, Height = 100 }, pos, null);
                platform.ApplyIntegerLock();
                return platform;
            }
            case ElementType.Trapezoid:
            {
                var trapezoid = new TrapezoidElement { TrapezoidType = 1, Width = 200, BaseHeight = 20 };
                trapezoid.ApplySlopeAngle();
                trapezoid.X = Math.Round(pos.X - trapezoid.Width / 2);
                trapezoid.Y = Math.Round(pos.Y - trapezoid.Height / 2);
                return trapezoid;
            }
            case ElementType.Camera:
                return new CameraElement { Name = "Camera", X = Math.Round(pos.X), Y = Math.Round(pos.Y) };
            case ElementType.Spawn:
                return new SpawnElement { Name = SpawnElement.DefaultName, X = Math.Round(pos.X), Y = Math.Round(pos.Y) };
            case ElementType.Model:
                return string.IsNullOrEmpty(source?.ModelName) ? null : new ModelElement { Name = source!.ModelName!, X = Math.Round(pos.X), Y = Math.Round(pos.Y) };
            case ElementType.Item:
            {
                var item = new ItemElement { X = Math.Round(pos.X), Y = Math.Round(pos.Y), ItemType = source?.ItemType ?? Models.Scene.ItemType.Bonus };
                item.Name = item.ItemType == Models.Scene.ItemType.Bonus ? "Bonus" : "Coin";
                return item;
            }
            case ElementType.Comment:
                return PlaceRect(new CommentElement { Name = "Comment", Width = 300, Height = 150 }, pos, null);
            case ElementType.BuildingIn:
            case ElementType.BuildingOut:
                return new BuildingMarkerElement(type == ElementType.BuildingIn) { X = Math.Round(pos.X), Y = Math.Round(pos.Y) };
        }
        return null;
    }

    private static SceneElement PlaceRect(SceneElement element, Point pos, Size? size)
    {
        if (size != null)
        {
            // Dragged out: the point is the top-left corner.
            element.X = pos.X;
            element.Y = pos.Y;
        }
        else
        {
            element.X = Math.Round(pos.X - element.Width / 2);
            element.Y = Math.Round(pos.Y - element.Height / 2);
        }
        return element;
    }

    // ================= NAMING RULES ================= //

    private IEnumerable<SpawnElement> SpawnScope(SceneElement spawn)
    {
        // Spawn names may repeat under different roots, but not within the same root.
        if (spawn.Parent == null)
            return Document.Roots.OfType<SpawnElement>();
        return SceneDocument.Walk(spawn.Root).OfType<SpawnElement>();
    }

    private static readonly Regex TrailingIndex = new(@"^(.*?)(_\d+|\(\d+\))$", RegexOptions.Compiled);

    /// <summary>
    /// Makes a spawn name unique within its root. Menu creation uses "Name(1)", duplicates use "Name_1".
    /// </summary>
    private string UniqueSpawnName(SpawnElement spawn, string desired, bool menuStyle)
    {
        var used = SpawnScope(spawn).Where(s => !ReferenceEquals(s, spawn)).Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        if (!used.Contains(desired))
            return desired;

        string baseName = desired;
        var match = TrailingIndex.Match(desired);
        if (match.Success && used.Contains(match.Groups[1].Value))
            baseName = match.Groups[1].Value;

        for (int i = 1; ; i++)
        {
            string candidate = menuStyle ? $"{baseName}({i})" : $"{baseName}_{i}";
            if (!used.Contains(candidate))
                return candidate;
        }
    }

    // ================= CAMERA RULES ================= //

    /// <summary>Returns an error when the camera's mode cannot change to <paramref name="value"/>, otherwise null.</summary>
    public string? ValidateCameraSelection(CameraElement camera, ModeSelection value)
    {
        var others = Document.AllElements().OfType<CameraElement>().Where(c => !ReferenceEquals(c, camera)).ToList();
        if (others.Count == 0)
            return null;
        if (value == ModeSelection.All)
            return Loc.T("Another Camera exists, so this Camera cannot apply to both modes. Delete the other Camera first.");
        if (others.Any(o => o.Selection == value || o.Selection == ModeSelection.All))
            return Loc.F("Another Camera already applies to {0}.", value == ModeSelection.HunterMode ? Loc.T("Hunter Mode") : Loc.T("Common Mode"));
        return null;
    }

    /// <summary>Gives pasted/duplicated cameras a free mode, or removes them when both modes are taken.</summary>
    private void FixPastedCameras(IEnumerable<SceneElement> pasted)
    {
        foreach (var camera in pasted.SelectMany(SceneDocument.Walk).OfType<CameraElement>().ToList())
        {
            var others = Document.AllElements().OfType<CameraElement>().Where(c => !ReferenceEquals(c, camera)).ToList();
            if (others.Count == 0)
                continue;
            bool commonTaken = others.Any(o => o.Selection is ModeSelection.CommonMode or ModeSelection.All);
            bool hunterTaken = others.Any(o => o.Selection is ModeSelection.HunterMode or ModeSelection.All);
            if (!commonTaken)
                camera.Selection = ModeSelection.CommonMode;
            else if (!hunterTaken)
                camera.Selection = ModeSelection.HunterMode;
            else
            {
                Document.Remove(camera);
                StatusText = Loc.T("A Camera was not pasted: both modes already have one.");
            }
        }
    }

    private void FixPastedSpawns(IEnumerable<SceneElement> pasted)
    {
        foreach (var spawn in pasted.SelectMany(SceneDocument.Walk).OfType<SpawnElement>().ToList())
            spawn.Name = UniqueSpawnName(spawn, string.IsNullOrEmpty(spawn.Name) ? SpawnElement.DefaultName : spawn.Name, menuStyle: false);
    }

    // ================= DELETE / CLIPBOARD ================= //

    private sealed record ClipboardItem(SceneElement Element, Point WorldPosition, string Layer);

    private static List<ClipboardItem> _clipboard = new();

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        // An auto-built platform takes its Object (or its loose textures) with it.
        var targets = ExpandSurfaceTargets(GetTopmostSelection(), includeLooseTiles: true);
        if (targets.Count == 0)
            return;
        ClearSelectionSilently();
        foreach (var element in targets)
            Document.Remove(element);
        RaiseStructureChanged();
        RaiseSelectionChanged();
        MarkChanged();
        StatusText = targets.Count == 1 ? Loc.F("Deleted {0}", targets[0].DisplayName) : Loc.F("Deleted {0} elements", targets.Count);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Copy()
    {
        var targets = GetTopmostSelection().Where(e => e is not BuildingMarkerElement).ToList();
        if (targets.Count == 0)
            return;
        _clipboard = targets.Select(e => new ClipboardItem(e.Clone(keepIds: false), e.WorldPosition, e.EffectiveLayer)).ToList();
        PasteCommand.NotifyCanExecuteChanged();
        PasteIntoCommand.NotifyCanExecuteChanged();
        StatusText = Loc.F("Copied {0} element(s)", targets.Count);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Cut()
    {
        Copy();
        Delete();
    }

    private bool CanPaste() => _clipboard.Count > 0;

    /// <summary>Paste at the same hierarchy level as the active element (or at the root).</summary>
    [RelayCommand(CanExecute = nameof(CanPaste))]
    private void Paste() => PasteAt(ActiveElement);

    public void PasteAt(SceneElement? sibling)
    {
        var parent = sibling?.Parent;
        int index = sibling != null ? Document.SiblingsOf(sibling).IndexOf(sibling) + 1 : -1;
        PasteInto(parent, index);
    }

    private bool CanPasteInto() => _clipboard.Count > 0 && ActiveElement is ObjectElement { IsReference: false };

    [RelayCommand(CanExecute = nameof(CanPasteInto))]
    private void PasteInto()
    {
        if (ActiveElement is ObjectElement obj)
            PasteInto(obj, -1);
    }

    public bool HasClipboard => _clipboard.Count > 0;

    /// <summary>
    /// Where an element goes when it is added under <paramref name="parent"/>: Objects with a definition cannot have
    /// children (under any circumstances), so it goes right after such an Object, at its level.
    /// </summary>
    public (ObjectElement? Parent, int Index) HostFor(ObjectElement? parent, int index)
    {
        while (parent is { IsReference: true } referenced)
        {
            index = Document.SiblingsOf(referenced).IndexOf(referenced) + 1;
            parent = referenced.Parent;
        }
        return (parent, index);
    }

    public void PasteInto(ObjectElement? parent, int index)
    {
        (parent, index) = HostFor(parent, index);
        if (_clipboard.Count == 0)
            return;
        var pasted = new List<SceneElement>();
        foreach (var item in _clipboard)
        {
            var copy = item.Element.Clone(keepIds: false);
            var origin = parent?.WorldPosition ?? new Point(0, 0);
            copy.X = item.WorldPosition.X - origin.X;
            copy.Y = item.WorldPosition.Y - origin.Y;
            copy.Layer = item.Layer;
            copy.ApplyIntegerLock();
            Document.Insert(copy, parent, index < 0 ? -1 : index++);
            pasted.Add(copy);
        }
        FixPastedCameras(pasted);
        FixPastedSpawns(pasted);
        // Editing an object: a pasted instance of it (or of anything containing it) would nest it inside itself.
        if (ObjectEdit != null)
            foreach (var obj in pasted.SelectMany(SceneDocument.Walk).OfType<ObjectElement>().Where(o => o.Reference != null))
                obj.Reference = SafeReference(obj.Name);
        pasted.RemoveAll(e => e.Parent == null && !Document.Roots.Contains(e));
        RaiseStructureChanged();
        MarkChanged();
        Select(pasted);
        if (pasted.Count > 0)
            RevealInHierarchy(pasted[^1]);
        StatusText = Loc.F("Pasted {0} element(s)", pasted.Count);
    }

    /// <summary>Ctrl+D: duplicates the selection in place, right after each original.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void Duplicate() => Select(DuplicateSelection());

    /// <summary>Duplicates the topmost selection (used by Ctrl+D and Alt+drag). Returns the copies.</summary>
    public List<SceneElement> DuplicateSelection()
    {
        var targets = GetTopmostSelection().Where(e => e is not BuildingMarkerElement).ToList();
        var copies = new List<SceneElement>();
        foreach (var selected in targets)
        {
            var element = selected;
            // An auto-built platform is duplicated with its Object: a new Object next to the original one.
            if (selected is PlatformElement { Surface: not null } surfaced)
            {
                if (SurfaceHost(surfaced) is { } host)
                {
                    var hostCopy = (ObjectElement)host.Clone(keepIds: false);
                    var hostSiblings = Document.SiblingsOf(host);
                    Document.Insert(hostCopy, host.Parent, hostSiblings.IndexOf(host) + 1);
                    copies.Add(hostCopy.Children.OfType<PlatformElement>().FirstOrDefault() ?? (SceneElement)hostCopy);
                    continue;
                }
                // No Object of its own yet: the copy (platform + textures) gets one.
                var wrapper = new ObjectElement { Name = string.Empty, Layer = surfaced.Layer, Order = surfaced.Order };
                var platformCopy = (PlatformElement)surfaced.Clone(keepIds: false);
                wrapper.AddChild(platformCopy);
                foreach (var tile in SurfaceTiles(surfaced))
                {
                    var tileCopy = (ImageElement)tile.Clone(keepIds: false);
                    tileCopy.SurfaceOwner = platformCopy.Id;
                    wrapper.AddChild(tileCopy);
                }
                var platformSiblings = Document.SiblingsOf(surfaced);
                Document.Insert(wrapper, surfaced.Parent, platformSiblings.IndexOf(surfaced) + 1);
                copies.Add(platformCopy);
                continue;
            }
            var copy = element.Clone(keepIds: false);
            var siblings = Document.SiblingsOf(element);
            Document.Insert(copy, element.Parent, siblings.IndexOf(element) + 1);
            if (!EditorPreferences.RenameDuplicate && copy.NameEditable && copy is not SpawnElement && !string.IsNullOrEmpty(copy.Name))
                copy.Name = UniqueName(copy.Name, siblings.Where(s => !ReferenceEquals(s, copy)).Select(s => s.Name));
            copies.Add(copy);
        }
        FixPastedCameras(copies);
        FixPastedSpawns(copies);
        copies.RemoveAll(e => e.Parent == null && !Document.Roots.Contains(e));
        RaiseStructureChanged();
        MarkChanged();
        return copies;
    }

    private static string UniqueName(string name, IEnumerable<string> existing)
    {
        var used = existing.ToHashSet(StringComparer.Ordinal);
        if (!used.Contains(name))
            return name;
        var match = Regex.Match(name, @"^(.*) \((\d+)\)$");
        string baseName = match.Success ? match.Groups[1].Value : name;
        for (int i = 1; ; i++)
        {
            string candidate = $"{baseName} ({i})";
            if (!used.Contains(candidate))
                return candidate;
        }
    }

    // ================= GROUP / RE-PARENT ================= //

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Group()
    {
        var targets = GetTopmostSelection();
        if (targets.Count == 0)
            return;

        // The group goes where the first selected element was.
        var first = targets[0];
        var parent = first.Parent;
        int index = Document.SiblingsOf(first).IndexOf(first);

        var worlds = targets.Select(t => t.WorldPosition).ToList();
        var origin = parent?.WorldPosition ?? new Point(0, 0);
        double minX = Math.Round(targets.Min(t => t.WorldPosition.X));
        double minY = Math.Round(targets.Min(t => t.WorldPosition.Y));

        var group = new ObjectElement
        {
            X = minX - origin.X,
            Y = minY - origin.Y,
            Layer = first.EffectiveLayer,
            IsExpanded = true,
        };

        Document.Insert(group, parent, index);
        for (int i = 0; i < targets.Count; i++)
        {
            var element = targets[i];
            Document.Remove(element);
            group.AddChild(element);
            element.X = worlds[i].X - minX;
            element.Y = worlds[i].Y - minY;
            element.ApplyIntegerLock();
        }

        RaiseStructureChanged();
        MarkChanged();
        Select(group);
        RevealInHierarchy(group);
        StatusText = Loc.F("Grouped {0} element(s)", targets.Count);
    }

    /// <summary>
    /// Moves elements under a new parent (null = root) at an index, keeping their positions in the level.
    /// Returns false when the move is impossible (an Object into its own descendant).
    /// </summary>
    public bool MoveElements(IReadOnlyList<SceneElement> elements, ObjectElement? newParent, int index)
    {
        if (newParent is { IsReference: true })
            return false; // Objects with a definition cannot have children
        var list = elements.Where(e => newParent == null || (!ReferenceEquals(e, newParent) && !newParent.IsDescendantOf(e))).ToList();
        if (list.Count == 0)
            return false;

        var siblings = newParent != null ? (IList<SceneElement>)newParent.Children : Document.Roots;
        var worlds = list.Select(e => (e.WorldPosition, e.EffectiveLayer)).ToList();
        var origin = newParent?.WorldPosition ?? new Point(0, 0);

        // Removing elements placed before the index shifts it.
        foreach (var element in list)
        {
            if (index >= 0 && ReferenceEquals(element.Parent, newParent) && siblings.IndexOf(element) is int i && i >= 0 && i < index)
                index--;
            Document.Remove(element);
        }

        for (int i = 0; i < list.Count; i++)
        {
            var element = list[i];
            Document.Insert(element, newParent, index < 0 ? -1 : index + i);
            element.X = worlds[i].WorldPosition.X - origin.X;
            element.Y = worlds[i].WorldPosition.Y - origin.Y;
            if (newParent == null)
                element.Layer = worlds[i].EffectiveLayer;
            element.ApplyIntegerLock();
        }

        RaiseStructureChanged();
        MarkChanged();
        return true;
    }

    /// <summary>Unity's Order in Layer for siblings is kept; this moves an element up/down among its siblings.</summary>
    public void MoveSibling(SceneElement element, int delta)
    {
        var siblings = Document.SiblingsOf(element);
        int i = siblings.IndexOf(element);
        int j = Math.Clamp(i + delta, 0, siblings.Count - 1);
        if (i == j)
            return;
        siblings.RemoveAt(i);
        siblings.Insert(j, element);
        RaiseStructureChanged();
        MarkChanged();
    }
}
