using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Vectorier.Models.Scene;
using Vectorier.ViewModels.Editor;

namespace Vectorier.Controls.Scene;

public enum SceneRenderMode
{
    Editor,
    Game,
    Thumbnail,
}

/// <summary>
/// Draws a <see cref="SceneDocument"/>. Keeps a flat list of render items indexed per parallax layer in a
/// spatial grid; every frame only the items whose full drawn bounds touch the viewport are sorted and drawn.
/// </summary>
public sealed partial class SceneRenderer
{
    /// <summary>Screen-space decorations (labels, outlines, gizmo text) never reach further than this from an item's bounds.</summary>
    public const double DecorationMarginPixels = 96;

    private static readonly SolidColorBrush TriggerFill = new(Color.FromArgb(0x40, 0xFF, 0xFF, 0x00));
    private static readonly Pen TriggerOutline = new(new SolidColorBrush(Color.FromArgb(0xFF, 0xCE, 0x7D, 0x14)), 1);
    private static readonly SolidColorBrush AreaFill = new(Color.FromArgb(0x40, 0xFF, 0x00, 0x00));
    private static readonly Pen AreaOutline = new(new SolidColorBrush(Color.FromArgb(0xFF, 0x80, 0x00, 0x00)), 1);
    private static readonly SolidColorBrush CollisionFill = new(Color.FromArgb(0x40, 0x00, 0x00, 0xFF));
    private static readonly Pen CollisionOutline = new(new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0x00, 0xFF)), 1);
    private static readonly SolidColorBrush CommentFill = new(Color.FromArgb(0x38, 0x00, 0xC8, 0x00));
    private static readonly Pen CommentOutline = new(new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0x8A, 0x00)), 1);
    private static readonly SolidColorBrush CommentText = new(Color.FromArgb(0xFF, 0x00, 0x55, 0x00));
    private static readonly Pen SelectionPen = new(new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x30)), 2);
    private static readonly Pen CameraPen = new(new SolidColorBrush(Color.FromArgb(0xC0, 0x40, 0x40, 0x40)), 1, new DashStyle(new double[] { 6, 4 }, 0));
    private static readonly SolidColorBrush CameraIconBrush = new(Color.FromRgb(0x30, 0x30, 0x30));
    private static readonly Pen MissingPen = new(new SolidColorBrush(Color.FromRgb(0xE0, 0x00, 0xA0)), 1, new DashStyle(new double[] { 4, 3 }, 0));
    private static readonly SolidColorBrush LabelBrush = new(Color.FromRgb(0x20, 0x20, 0x20));
    private static readonly SolidColorBrush ModelBrush = new(Colors.Black);
    private static readonly SolidColorBrush ItemFallback = new(Color.FromArgb(0xC0, 0xF0, 0xB0, 0x00));
    private static readonly Typeface LabelTypeface = new(FontFamily.Default);

    private readonly EditorViewModel _editor;
    private readonly List<RenderItem> _items = new();
    private readonly List<LayerGroup> _groups = new();
    private readonly Dictionary<SceneElement, List<RenderItem>> _byElement = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SceneElement, List<RenderItem>> _byInstance = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<RenderItem> _dirty = new();
    private readonly List<RenderItem> _visible = new();
    private readonly List<RenderItem> _overlays = new();
    // Reused every frame (no per-frame allocations while panning or dragging).
    private readonly List<RenderItem> _candidates = new();
    private Matrix[] _matrices = Array.Empty<Matrix>();
    // Recorded labels by (text, size, color, wrap width).
    private readonly Dictionary<(string, double, uint, double), LabelText> _texts = new();
    private readonly Dictionary<string, (Geometry? Geometry, Rect Bounds, bool Loaded)> _models = new(StringComparer.OrdinalIgnoreCase);
    private bool _needsRebuild = true;
    private int _stamp;

    public SceneRenderer(EditorViewModel editor)
    {
        _editor = editor;
    }

    /// <summary>Raised when something loaded asynchronously needs a redraw.</summary>
    public event Action? RedrawRequested;

    /// <summary>True when the last frame drew an animated texture (the view then keeps redrawing at 20 FPS).</summary>
    public bool HasVisibleAnimation { get; private set; }

    /// <summary>Number of items drawn in the last frame (for diagnostics).</summary>
    public int LastDrawnCount { get; private set; }

    public IReadOnlyList<LayerGroup> Groups => _groups;

    public void InvalidateStructure() => _needsRebuild = true;

    /// <summary>Marks the items of an element (and of everything below it) for new bounds.</summary>
    public void InvalidateElement(SceneElement element)
    {
        if (_needsRebuild)
            return;
        foreach (var e in SceneDocument.Walk(element))
        {
            if (_byElement.TryGetValue(e, out var list))
            {
                foreach (var item in list)
                    MarkDirty(item);
            }
            if (_byInstance.TryGetValue(e, out var ghosts))
            {
                foreach (var item in ghosts)
                    MarkDirty(item);
            }
        }
    }

    private void MarkDirty(RenderItem item)
    {
        if (!item.Dirty)
        {
            item.Dirty = true;
            _dirty.Add(item);
        }
    }

    // ================= BUILD ================= //

    private long _traversal;

    private void Rebuild()
    {
        _needsRebuild = false;
        foreach (var group in _groups)
            group.Grid.Clear();
        _groups.Clear();
        _items.Clear();
        _byElement.Clear();
        _byInstance.Clear();
        _dirty.Clear();
        _traversal = 0;

        foreach (var root in Ordered(_editor.Document.Roots))
        {
            var group = GetGroup(Layers.GetFactor(root.LayerEditable ? root.Layer : Layers.Default));
            Visit(root, root, group, null, default, 0);
        }

        // Lower factors are further away and are drawn first.
        _groups.Sort((a, b) => a.Factor.CompareTo(b.Factor));
        for (int i = 0; i < _groups.Count; i++)
            _groups[i].Index = i;

        foreach (var item in _items)
        {
            item.SortKey = ComputeSortKey(item);
            item.Bounds = ComputeBounds(item);
            item.Dirty = false;
            item.Group.Grid.Insert(item);
        }
    }

    /// <summary>Siblings in drawing order: Order in Layer first, then hierarchy order (same as the XML writer).</summary>
    private static IEnumerable<SceneElement> Ordered(IList<SceneElement> elements)
    {
        if (elements.Count < 2)
            return elements;
        return elements.Select((e, i) => (e, i)).OrderBy(t => t.e.Order).ThenBy(t => t.i).Select(t => t.e);
    }

    private LayerGroup GetGroup(double factor)
    {
        foreach (var g in _groups)
        {
            if (Math.Abs(g.Factor - factor) < 1e-9)
                return g;
        }
        var group = new LayerGroup(factor, _groups.Count);
        _groups.Add(group);
        return group;
    }

    private void Visit(SceneElement element, SceneElement owner, LayerGroup group, ObjectElement? instance, Vector ghostOffset, int depth)
    {
        if (depth > 48)
            return;

        if (element is ObjectElement obj)
        {
            // Content resolved from <Sets> is drawn first, then the inline children.
            if (obj.Reference is { } definition)
            {
                var inst = instance ?? obj;
                var offset = instance == null ? default : ghostOffset + new Vector(obj.X, obj.Y);
                foreach (var content in Ordered(definition.Content))
                    Visit(content, owner, group, inst, offset, depth + 1);
            }

            var childOffset = instance == null ? default : ghostOffset + new Vector(obj.X, obj.Y);
            foreach (var child in Ordered(obj.Children))
                Visit(child, instance == null ? child : owner, group, instance, childOffset, depth + 1);
            // A level Object whose definition is gone is drawn (and picked) as a MISSING box.
            if (instance != null || !obj.IsMissingDefinition)
                return;
        }

        var item = new RenderItem
        {
            Element = element,
            Owner = owner,
            Instance = instance,
            GhostOffset = ghostOffset,
            Group = group,
            IsOverlay = IsOverlayType(element.Type),
        };
        item.SortKey = _traversal++;
        _items.Add(item);

        var map = instance == null ? _byElement : _byInstance;
        var key = instance == null ? element : instance;
        if (!map.TryGetValue(key, out var list))
        {
            list = new List<RenderItem>(1);
            map[key] = list;
        }
        list.Add(item);
    }

    private static bool IsOverlayType(ElementType type) => type is ElementType.Platform or ElementType.Trapezoid or ElementType.Area
        or ElementType.BuildingIn or ElementType.BuildingOut
        or ElementType.Trigger or ElementType.Spawn or ElementType.Item or ElementType.Camera or ElementType.Comment;

    private static int OverlayCategory(ElementType type) => type switch
    {
        ElementType.Platform or ElementType.Trapezoid => 0,
        ElementType.Area => 1,
        ElementType.Trigger => 2,
        ElementType.Spawn => 3,
        ElementType.Item => 4,
        ElementType.Camera => 5,
        ElementType.BuildingIn or ElementType.BuildingOut => 7, // above everything
        _ => 6, // Comments above everything
    };

    /// <summary>Game drawing order inside a layer: Back images, Middle, Front, then particles and models.</summary>
    private static int DepthGroup(SceneElement element) => element switch
    {
        ImageElement { Depth: ImageDepth.Back } => 0,
        ImageElement { Depth: ImageDepth.Front } => 2,
        ParticleElement => 3,
        ModelElement => 4,
        _ => 1,
    };

    private long ComputeSortKey(RenderItem item)
    {
        long traversal = item.SortKey & 0xFFFFFFFFL;
        return item.IsOverlay
            ? ((long)OverlayCategory(item.Element.Type) << 48) | ((long)item.Group.Index << 40) | traversal
            : ((long)item.Group.Index << 48) | ((long)DepthGroup(item.Element) << 40) | traversal;
    }

    // ================= GEOMETRY ================= //

    /// <summary>Level-space point that the element's local coordinates are relative to.</summary>
    public static Point Origin(RenderItem item)
    {
        if (item.Instance != null)
            return item.Instance.WorldPosition + item.GhostOffset;
        return item.Element.ParentWorldPosition;
    }

    private Rect ComputeBounds(RenderItem item)
    {
        var e = item.Element;
        var o = Origin(item);
        Rect local;
        switch (e)
        {
            case CameraElement camera:
                {
                    var view = CameraElement.ViewSize;
                    local = new Rect(camera.X - view.Width / 2, camera.Y - view.Height / 2, view.Width, view.Height).Union(camera.GetLocalBounds());
                    break;
                }
            case ItemElement it:
                {
                    local = it.GetLocalBounds();
                    var asset = _editor.Context.Textures.TryGet(ItemTexture(it));
                    if (asset != null)
                        local = local.Union(new Rect(it.X - asset.Width / 2.0, it.Y - asset.Height / 2.0, asset.Width, asset.Height));
                    break;
                }
            case ModelElement model:
                local = model.GetLocalBounds();
                if (_models.TryGetValue(model.ClassName, out var m) && m.Geometry != null)
                    local = new Rect(model.X + m.Bounds.X, model.Y + m.Bounds.Y, m.Bounds.Width, m.Bounds.Height);
                break;
            default:
                local = e.GetLocalBounds();
                break;
        }
        return local.Translate(new Vector(o.X, o.Y));
    }

    private void ProcessDirty()
    {
        if (_dirty.Count == 0)
            return;
        foreach (var item in _dirty)
        {
            item.Group.Grid.Remove(item);
            item.Bounds = ComputeBounds(item);
            item.Dirty = false;
            item.Group.Grid.Insert(item);
        }
        _dirty.Clear();
    }

    /// <summary>Bounds of an element in its layer's level space, for selection outlines and framing.</summary>
    public static Rect WorldBounds(SceneElement element)
    {
        var o = element.ParentWorldPosition;
        Rect local = element is CameraElement c ? c.GetLocalBounds() : element.GetLocalBounds();
        return local.Translate(new Vector(o.X, o.Y));
    }

    public LayerGroup GroupFor(SceneElement element)
    {
        var root = element.Root;
        double factor = Layers.GetFactor(root.LayerEditable ? root.Layer : Layers.Default);
        foreach (var g in _groups)
        {
            if (Math.Abs(g.Factor - factor) < 1e-9)
                return g;
        }
        return new LayerGroup(factor, -1);
    }

    private bool IsHidden(RenderItem item)
    {
        // Eye in the Hierarchy (on the element, an ancestor, or the object whose content this is).
        if ((item.Instance ?? item.Element).IsHiddenInScene)
            return true;
        // Images being painted: the Brush canvas shows them instead.
        if (_editor.Paint.IsHidden(item.Element))
            return true;
        if (!_editor.IsTypeVisible(item.Element.Type))
            return true;
        if (item.Instance != null && !_editor.IsTypeVisible(ElementType.Object))
            return true;
        return false;
    }

    /// <summary>
    /// Locked in the Hierarchy (the element, an ancestor, or the object whose content this is): Scene picking passes
    /// through it to whatever is behind.
    /// </summary>
    private static bool IsLocked(RenderItem item) =>
        (item.Instance ?? item.Element).IsLockedInScene || item.Owner.IsLockedInScene;
}
