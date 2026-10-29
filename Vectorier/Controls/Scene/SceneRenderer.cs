using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Vectorier.Models.Scene;
using Vectorier.Services;
using Vectorier.Services.Assets;
using Vectorier.Services.IO;
using Vectorier.ViewModels.Editor;

namespace Vectorier.Controls.Scene;

/// <summary>A drawable piece of the scene: one element, or one element of a referenced object's content.</summary>
public sealed class RenderItem
{
    /// <summary>The element that is drawn.</summary>
    public SceneElement Element = null!;

    /// <summary>The element of the level that is selected when this item is clicked.</summary>
    public SceneElement Owner = null!;

    /// <summary>For content resolved from &lt;Sets&gt;: the level Object whose definition this belongs to.</summary>
    public ObjectElement? Instance;

    /// <summary>For resolved content: position of the element's parent inside the definition, relative to the instance.</summary>
    public Vector GhostOffset;

    public LayerGroup Group = null!;
    public bool IsOverlay;
    public long SortKey;

    /// <summary>Everything the item can draw (image, outline, gizmo...), in the level space of its layer.</summary>
    public Rect Bounds;

    public bool Dirty = true;

    // Spatial grid bookkeeping.
    internal bool InGrid;
    internal bool Large;
    internal int CellX0, CellY0, CellX1, CellY1;
    internal int Stamp;

    public bool IsGhost => Instance != null;
}

/// <summary>Elements sharing one parallax factor. Each layer has its own transform and spatial index.</summary>
public sealed class LayerGroup
{
    public LayerGroup(double factor, int index)
    {
        Factor = factor;
        Index = index;
        FrameScale = Layers.GetFrameScale(factor);
    }

    public double Factor { get; }
    public int Index { get; set; }

    /// <summary>Parallax scale of the layer at zoom 1 (Unity Parallax defaults).</summary>
    public double FrameScale { get; }

    internal readonly SpatialGrid Grid = new();
}

/// <summary>
/// Uniform-grid spatial index over level-space bounds. Viewport queries touch only the cells that the view
/// overlaps, so the cost of a frame depends on what is visible, not on the size of the level.
/// </summary>
internal sealed class SpatialGrid
{
    private const double CellSize = 512;
    private const int LargeCellSpan = 48;

    private readonly Dictionary<long, HashSet<RenderItem>> _cells = new();
    private readonly HashSet<RenderItem> _large = new();

    private static long Key(int x, int y) => ((long)x << 32) | (uint)y;

    private static int Cell(double v) => (int)Math.Floor(v / CellSize);

    public void Clear()
    {
        _cells.Clear();
        _large.Clear();
    }

    public void Insert(RenderItem item)
    {
        var b = item.Bounds;
        item.CellX0 = Cell(b.Left);
        item.CellY0 = Cell(b.Top);
        item.CellX1 = Cell(b.Right);
        item.CellY1 = Cell(b.Bottom);
        item.Large = item.CellX1 - item.CellX0 > LargeCellSpan || item.CellY1 - item.CellY0 > LargeCellSpan;
        item.InGrid = true;
        if (item.Large)
        {
            _large.Add(item);
            return;
        }
        for (int x = item.CellX0; x <= item.CellX1; x++)
        {
            for (int y = item.CellY0; y <= item.CellY1; y++)
            {
                long key = Key(x, y);
                if (!_cells.TryGetValue(key, out var set))
                {
                    set = new HashSet<RenderItem>();
                    _cells[key] = set;
                }
                set.Add(item);
            }
        }
    }

    public void Remove(RenderItem item)
    {
        if (!item.InGrid)
            return;
        item.InGrid = false;
        if (item.Large)
        {
            _large.Remove(item);
            return;
        }
        for (int x = item.CellX0; x <= item.CellX1; x++)
        {
            for (int y = item.CellY0; y <= item.CellY1; y++)
            {
                if (_cells.TryGetValue(Key(x, y), out var set))
                {
                    set.Remove(item);
                    if (set.Count == 0)
                        _cells.Remove(Key(x, y));
                }
            }
        }
    }

    /// <summary>Adds every item whose bounds intersect <paramref name="rect"/> (each once per stamp).</summary>
    public void Query(Rect rect, int stamp, List<RenderItem> output)
    {
        foreach (var item in _large)
        {
            if (item.Stamp != stamp && item.Bounds.Intersects(rect))
            {
                item.Stamp = stamp;
                output.Add(item);
            }
        }

        int x0 = Cell(rect.Left), y0 = Cell(rect.Top), x1 = Cell(rect.Right), y1 = Cell(rect.Bottom);
        long cellCount = (long)(x1 - x0 + 1) * (y1 - y0 + 1);
        if (cellCount > _cells.Count * 2L)
        {
            // Zoomed far out: walking the occupied cells is cheaper than walking the rectangle.
            foreach (var set in _cells.Values)
                AddFrom(set, rect, stamp, output);
            return;
        }
        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                if (_cells.TryGetValue(Key(x, y), out var set))
                    AddFrom(set, rect, stamp, output);
            }
        }
    }

    private static void AddFrom(HashSet<RenderItem> set, Rect rect, int stamp, List<RenderItem> output)
    {
        foreach (var item in set)
        {
            if (item.Stamp == stamp)
                continue;
            item.Stamp = stamp;
            if (item.Bounds.Intersects(rect))
                output.Add(item);
        }
    }
}

/// <summary>Screen mapping of the Scene view: a level/display point at the view center and a zoom.</summary>
public readonly record struct SceneViewport(Point Center, double Zoom, Size Size)
{
    public Point ScreenCenter => new(Size.Width / 2, Size.Height / 2);

    /// <summary>Parallax offset of a layer: the view center is the reference point (Unity: the camera).</summary>
    public Vector LayerOffset(LayerGroup group) => new(Center.X * (1 - group.Factor * group.FrameScale), Center.Y * (1 - group.Factor * group.FrameScale));

    /// <summary>Level space of a layer → screen pixels.</summary>
    public Matrix LevelToScreen(LayerGroup group)
    {
        double k = group.FrameScale * Zoom;
        var offset = LayerOffset(group);
        double tx = (offset.X - Center.X) * Zoom + Size.Width / 2;
        double ty = (offset.Y - Center.Y) * Zoom + Size.Height / 2;
        return new Matrix(k, 0, 0, k, tx, ty);
    }

    public Point ScreenToLevel(Point screen, LayerGroup group)
    {
        double k = group.FrameScale * Zoom;
        var m = LevelToScreen(group);
        return new Point((screen.X - m.M31) / k, (screen.Y - m.M32) / k);
    }

    /// <summary>Level-space rectangle of a layer that is visible on screen, grown by a margin in pixels.</summary>
    public Rect VisibleLevelRect(LayerGroup group, double marginPixels)
    {
        var a = ScreenToLevel(new Point(-marginPixels, -marginPixels), group);
        var b = ScreenToLevel(new Point(Size.Width + marginPixels, Size.Height + marginPixels), group);
        return RectEx.Box(a, b);
    }
}

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
public sealed class SceneRenderer
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
    private readonly Dictionary<(string, double, uint, double), FormattedText> _texts = new();
    private readonly Dictionary<string, (Geometry? Geometry, Rect Bounds, bool Loaded)> _models = new(StringComparer.OrdinalIgnoreCase);
    private bool _needsRebuild = true;
    private int _stamp;
    private static Bitmap? _mark;

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

    public int TotalItemCount => _items.Count;

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

    // ================= QUERIES (selection) ================= //

    /// <summary>Topmost item under a screen point (overlays first, then the nearest layer).</summary>
    public RenderItem? HitTest(SceneViewport viewport, Point screen)
    {
        if (_needsRebuild)
            Rebuild();
        ProcessDirty();

        RenderItem? best = null;
        var candidates = new List<RenderItem>();
        int stamp = ++_stamp;
        foreach (var group in _groups)
        {
            var p = viewport.ScreenToLevel(screen, group);
            double tolerance = 3 / (viewport.Zoom * group.FrameScale);
            group.Grid.Query(new Rect(p.X - tolerance, p.Y - tolerance, tolerance * 2, tolerance * 2), stamp, candidates);
        }

        foreach (var item in candidates)
        {
            if (IsHidden(item))
                continue;
            var p = viewport.ScreenToLevel(screen, item.Group);
            double tolerance = 3 / (viewport.Zoom * item.Group.FrameScale);
            if (!Contains(item, p, tolerance))
                continue;
            if (best == null || PickAbove(item, best))
                best = item;
        }
        return best;
    }

    /// <summary>Every element under a screen point (deepest owners), topmost first, without duplicates.</summary>
    public List<SceneElement> HitTestAll(SceneViewport viewport, Point screen)
    {
        if (_needsRebuild)
            Rebuild();
        ProcessDirty();
        var candidates = new List<RenderItem>();
        int stamp = ++_stamp;
        foreach (var group in _groups)
        {
            var p = viewport.ScreenToLevel(screen, group);
            double tolerance = 3 / (viewport.Zoom * group.FrameScale);
            group.Grid.Query(new Rect(p.X - tolerance, p.Y - tolerance, tolerance * 2, tolerance * 2), stamp, candidates);
        }
        var hits = new List<RenderItem>();
        foreach (var item in candidates)
        {
            if (IsHidden(item))
                continue;
            var p = viewport.ScreenToLevel(screen, item.Group);
            double tolerance = 3 / (viewport.Zoom * item.Group.FrameScale);
            if (Contains(item, p, tolerance))
                hits.Add(item);
        }
        hits.Sort((a, b) => PickAbove(a, b) ? -1 : PickAbove(b, a) ? 1 : 0);
        var result = new List<SceneElement>();
        foreach (var item in hits)
            if (!result.Contains(item.Owner))
                result.Add(item.Owner);
        return result;
    }

    /// <summary>
    /// Picking priority (what a click selects first): gameplay elements by type (Comment, Item, Camera, Spawn,
    /// Trigger, Area, Platform, Trapezoid), then visuals by layer (front first), order in layer, type (Image,
    /// Animation, Particle, Object) and finally drawing order.
    /// </summary>
    private static bool PickAbove(RenderItem a, RenderItem b)
    {
        int pa = PickPriority(a), pb = PickPriority(b);
        bool gameplayA = pa < 9, gameplayB = pb < 9;
        if (gameplayA != gameplayB)
            return gameplayA;
        if (gameplayA)
            return pa != pb ? pa < pb : a.SortKey > b.SortKey;
        if (a.Group.Index != b.Group.Index)
            return a.Group.Index > b.Group.Index;
        int oa = a.Owner.Root.Order, ob = b.Owner.Root.Order;
        if (oa != ob)
            return oa > ob;
        if (pa != pb)
            return pa < pb;
        return a.SortKey > b.SortKey;
    }

    private static int PickPriority(RenderItem item)
    {
        // Content of a referenced Object selects the Object: it ranks as an Object.
        var type = item.Owner is ObjectElement ? ElementType.Object : item.Element.Type;
        return type switch
        {
            ElementType.BuildingIn or ElementType.BuildingOut => 0,
            ElementType.Comment => 1,
            ElementType.Item => 2,
            ElementType.Camera => 3,
            ElementType.Spawn => 4,
            ElementType.Trigger => 5,
            ElementType.Area => 6,
            ElementType.Platform => 7,
            ElementType.Trapezoid => 8,
            ElementType.Image => 9,
            ElementType.Animation => 10,
            ElementType.Particle => 11,
            ElementType.Model => 11,
            ElementType.Object => 12,
            _ => 13,
        };
    }

    private static bool Above(RenderItem a, RenderItem b)
    {
        if (a.IsOverlay != b.IsOverlay)
            return a.IsOverlay;
        return a.SortKey > b.SortKey;
    }

    private static bool Contains(RenderItem item, Point p, double tolerance)
    {
        var e = item.Element;
        var o = Origin(item);
        var local = new Point(p.X - o.X, p.Y - o.Y);
        Point[] corners = e is PointElement pe ? pe.GetLocalCorners() : e.GetLocalCorners();
        if (e is ModelElement { SilhouetteBounds: { } sb } model)
            corners = new[] { new Point(model.X + sb.Left, model.Y + sb.Top), new Point(model.X + sb.Right, model.Y + sb.Top), new Point(model.X + sb.Right, model.Y + sb.Bottom), new Point(model.X + sb.Left, model.Y + sb.Bottom) };
        return PointInPolygon(corners, local, tolerance);
    }

    private static bool PointInPolygon(Point[] poly, Point p, double tolerance)
    {
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        foreach (var c in poly)
        {
            if (c.X < minX) minX = c.X;
            if (c.X > maxX) maxX = c.X;
            if (c.Y < minY) minY = c.Y;
            if (c.Y > maxY) maxY = c.Y;
        }
        minX -= tolerance; maxX += tolerance; minY -= tolerance; maxY += tolerance;
        if (p.X < minX || p.X > maxX || p.Y < minY || p.Y > maxY)
            return false;
        // Degenerate shapes (lines/points) are picked by their bounds.
        if (maxX - minX <= tolerance * 2.5 || maxY - minY <= tolerance * 2.5)
            return true;

        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) &&
                p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>Top-level elements whose items intersect a screen rectangle (rubber-band selection).</summary>
    public List<SceneElement> QueryRect(SceneViewport viewport, Rect screenRect)
    {
        if (_needsRebuild)
            Rebuild();
        ProcessDirty();

        var result = new List<SceneElement>();
        var seen = new HashSet<SceneElement>(ReferenceEqualityComparer.Instance);
        var candidates = new List<RenderItem>();
        int stamp = ++_stamp;
        foreach (var group in _groups)
        {
            var a = viewport.ScreenToLevel(screenRect.TopLeft, group);
            var b = viewport.ScreenToLevel(screenRect.BottomRight, group);
            group.Grid.Query(RectEx.Box(a, b), stamp, candidates);
        }
        foreach (var item in candidates)
        {
            if (IsHidden(item))
                continue;
            // Cameras are picked by their icon, not by their whole view rectangle.
            if (item.Element is CameraElement)
            {
                var o = Origin(item);
                var hb = item.Element.GetLocalBounds().Translate(new Vector(o.X, o.Y));
                var a = viewport.ScreenToLevel(screenRect.TopLeft, item.Group);
                var b = viewport.ScreenToLevel(screenRect.BottomRight, item.Group);
                if (!hb.Intersects(RectEx.Box(a, b)))
                    continue;
            }
            var root = item.Owner.Root;
            if (seen.Add(root))
                result.Add(root);
        }
        return result;
    }

    // ================= DRAWING ================= //

    /// <summary>Highest parallax factor drawn before <c>afterGameplay</c> (the gameplay layer is 1; Overlay is 1.00001).</summary>
    public const double GameplayFactor = 1.0;

    /// <param name="afterGameplay">
    /// Drawn after the visuals of the gameplay layer (and every layer behind it) and before the Overlay layer and
    /// the layers in front of it (the Move Visualizer's silhouette).
    /// </param>
    public void Render(DrawingContext context, SceneViewport viewport, Action<DrawingContext>? afterGameplay = null)
        => Render(context, viewport, SceneRenderMode.Editor, afterGameplay);

    /// <param name="mode">
    /// Editor draws everything the eye toggles allow. Game draws what the game shows (no gameplay overlays, eye
    /// toggles ignored). Thumbnail draws only images (from Objects and Images).
    /// </param>
    public void Render(DrawingContext context, SceneViewport viewport, SceneRenderMode mode, Action<DrawingContext>? afterGameplay = null)
    {
        if (_needsRebuild)
            Rebuild();
        ProcessDirty();

        _visible.Clear();
        _overlays.Clear();
        int stamp = ++_stamp;
        var candidates = _candidates;
        foreach (var group in _groups)
        {
            candidates.Clear();
            group.Grid.Query(viewport.VisibleLevelRect(group, DecorationMarginPixels), stamp, candidates);
            foreach (var item in candidates)
            {
                switch (mode)
                {
                    case SceneRenderMode.Editor:
                        if (IsHidden(item))
                            continue;
                        break;
                    case SceneRenderMode.Game:
                        if (item.IsOverlay)
                            continue;
                        break;
                    default:
                        if (item.IsOverlay || item.Element is not (ImageElement or ModelElement))
                            continue;
                        break;
                }
                (item.IsOverlay ? _overlays : _visible).Add(item);
            }
        }

        _visible.Sort((a, b) => a.SortKey.CompareTo(b.SortKey));
        _overlays.Sort((a, b) => a.SortKey.CompareTo(b.SortKey));

        bool animated = false;
        double seconds = AnimationClock.Seconds;
        if (_matrices.Length < _groups.Count)
            _matrices = new Matrix[_groups.Count];
        var matrices = _matrices;
        for (int i = 0; i < _groups.Count; i++)
            matrices[i] = viewport.LevelToScreen(_groups[i]);

        // Origin marker under everything (editor only, not an element).
        if (mode == SceneRenderMode.Editor)
            DrawMarker(context, viewport.LevelToScreen(new LayerGroup(GameplayFactor, -1)), new Point(0, 0), Colors.LimeGreen);

        bool inserted = afterGameplay == null;
        foreach (var item in _visible)
        {
            if (!inserted && item.Group.Factor > GameplayFactor + 1e-12)
            {
                afterGameplay!(context);
                inserted = true;
            }
            animated |= DrawVisual(context, item, matrices[item.Group.Index], seconds);
        }
        if (!inserted)
            afterGameplay!(context);
        foreach (var item in _overlays)
            animated |= DrawOverlay(context, item, matrices[item.Group.Index], viewport, seconds);

        HasVisibleAnimation = animated;
        LastDrawnCount = _visible.Count + _overlays.Count;
    }

    private bool DrawVisual(DrawingContext context, RenderItem item, Matrix m, double seconds)
    {
        var e = item.Element;
        var o = Origin(item);
        switch (e)
        {
            case ImageElement image:
                return DrawImage(context, item, image, o, m, seconds);
            case AnimationElement or ParticleElement:
                return DrawTexturedRect(context, item, e, o, m, seconds);
            case ModelElement model:
                DrawModel(context, item, model, o, m);
                return false;
            case ObjectElement { IsMissingDefinition: true } missing:
                DrawMissing(context, missing.GetLocalCorners(), o, m);
                return false;
        }
        return false;
    }

    private bool DrawImage(DrawingContext context, RenderItem item, ImageElement image, Point o, Matrix m, double seconds)
    {
        var textures = _editor.Context.Textures;
        var asset = textures.TryGet(image.ClassName);
        if (asset == null)
        {
            if (textures.IsMissing(image.ClassName))
                DrawMissing(context, image.GetLocalCorners(), o, m);
            return false;
        }

        if (image.AutoSize && (Math.Abs(image.Width - asset.Width) > 1e-6 || Math.Abs(image.Height - asset.Height) > 1e-6))
        {
            // The game uses the texture size when the XML has none.
            using (_editor.SuppressTracking())
                image.FillNaturalSize(asset.Width, asset.Height);
            MarkDirty(item);
            if (item.Instance?.Reference is { } def)
                def.ComputeBounds();
            RedrawRequested?.Invoke();
        }

        var (a, b, c, d) = image.GetBasis();
        var local = new Matrix(a, b, c, d, o.X + image.X, o.Y + image.Y);
        DrawAsset(context, asset, local * m, image.Color, seconds);
        return asset.IsAnimated;
    }

    private bool DrawTexturedRect(DrawingContext context, RenderItem item, SceneElement e, Point o, Matrix m, double seconds)
    {
        var textures = _editor.Context.Textures;
        var asset = textures.TryGet(e.Name);
        if (asset == null)
        {
            if (textures.IsMissing(e.Name))
                DrawMissing(context, e.GetLocalCorners(), o, m);
            return false;
        }
        var local = new Matrix(e.Width, 0, 0, e.Height, o.X + e.X, o.Y + e.Y);
        DrawAsset(context, asset, local * m, Colors.White, seconds);
        return asset.IsAnimated;
    }

    /// <summary>Draws a texture mapped onto the unit square transformed by <paramref name="unitToScreen"/>.</summary>
    private static void DrawAsset(DrawingContext context, TextureAsset asset, Matrix unitToScreen, Color color, double seconds)
    {
        if (color.A == 0)
            return;
        var tinted = TintCache.NeedsTint(color) ? TintCache.Get(asset.Bitmap, color) : null;
        using (context.PushTransform(unitToScreen))
        {
            if (color.A < 255)
            {
                using (context.PushOpacity(color.A / 255.0))
                    asset.Draw(context, new Rect(0, 0, 1, 1), asset.FrameAt(seconds), tinted);
            }
            else
            {
                asset.Draw(context, new Rect(0, 0, 1, 1), asset.FrameAt(seconds), tinted);
            }
        }
    }

    private static readonly IBrush MissingTextBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x1C, 0x23));
    private static readonly Typeface MissingTypeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);
    private FormattedText? _missingText;

    /// <summary>Missing texture: red outline and cross with a bold "Missing" scaled to the image's size.</summary>
    private void DrawMissing(DrawingContext context, Point[] corners, Point o, Matrix m)
    {
        var pts = corners.Select(c => new Point(c.X + o.X, c.Y + o.Y).Transform(m)).ToArray();
        DrawPolygon(context, null, MissingPen, pts);
        if (pts.Length == 4)
        {
            context.DrawLine(MissingPen, pts[0], pts[2]);
            context.DrawLine(MissingPen, pts[1], pts[3]);
        }

        double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
        double w = maxX - minX, h = maxY - minY;
        // Reference text at size 100, scaled to fit 70 % of the width and 40 % of the height (padding around it).
        _missingText ??= new FormattedText("Missing", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MissingTypeface, 100, MissingTextBrush);
        double scale = Math.Min(w * 0.7 / _missingText.Width, h * 0.4 / _missingText.Height);
        if (scale * 100 < 7)
            return;
        var center = new Point((minX + maxX) / 2, (minY + maxY) / 2);
        var bg = new Rect(center.X - _missingText.Width * scale / 2 - 4 * scale * 10, center.Y - _missingText.Height * scale / 2,
            _missingText.Width * scale + 8 * scale * 10, _missingText.Height * scale);
        context.FillRectangle(new SolidColorBrush(Colors.White, 0.75), bg);
        using (context.PushTransform(Matrix.CreateTranslation(-_missingText.Width / 2, -_missingText.Height / 2) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(center.X, center.Y)))
            context.DrawText(_missingText, new Point(0, 0));
    }

    private void DrawModel(DrawingContext context, RenderItem item, ModelElement model, Point o, Matrix m)
    {
        var entry = GetModel(model.ClassName);
        if (entry.Geometry == null)
        {
            // Not loaded yet, unreadable, or skipped: a marker at the pivot.
            var center = new Point(o.X + model.X, o.Y + model.Y).Transform(m);
            context.DrawEllipse(null, new Pen(ModelBrush, 1, new DashStyle(new double[] { 3, 3 }, 0)), center, 10, 10);
            return;
        }
        if (model.SilhouetteBounds != entry.Bounds)
        {
            model.SilhouetteBounds = entry.Bounds;
            MarkDirty(item);
        }
        using (context.PushTransform(Matrix.CreateTranslation(o.X + model.X, o.Y + model.Y) * m))
            context.DrawGeometry(ModelBrush, null, entry.Geometry);
    }

    private (Geometry? Geometry, Rect Bounds, bool Loaded) GetModel(string name)
    {
        if (string.IsNullOrEmpty(name))
            return (null, default, true);
        if (_models.TryGetValue(name, out var entry))
            return entry;
        _models[name] = (null, default, false);
        LoadModel(name);
        return (null, default, false);
    }

    private async void LoadModel(string name)
    {
        Geometry? geometry = null;
        Rect bounds = default;
        try
        {
            var data = await _editor.Context.Models.GetAsync(name);
            if (data != null && data.IsSelfContained)
                geometry = data.BuildSilhouette(null, out bounds);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Scene] model {name}: {ex.Message}");
        }
        _models[name] = (geometry, bounds, true);
        foreach (var item in _items)
        {
            if (item.Element is ModelElement model && string.Equals(model.ClassName, name, StringComparison.OrdinalIgnoreCase))
            {
                model.SilhouetteBounds = geometry != null ? bounds : null;
                MarkDirty(item);
            }
        }
        RedrawRequested?.Invoke();
    }

    /// <summary>Model silhouettes are cached per name; this forgets one (e.g. after the file changed).</summary>
    public void ForgetModel(string name) => _models.Remove(name);

    private bool DrawOverlay(DrawingContext context, RenderItem item, Matrix m, SceneViewport viewport, double seconds)
    {
        var e = item.Element;
        var o = Origin(item);
        switch (e)
        {
            case PlatformElement:
            {
                var r = ScreenRect(e, o, m);
                DrawBox(context, r, CollisionFill, EditorPreferences.ShowPlatformOutline ? CollisionOutline : null);
                return false;
            }
            case TrapezoidElement trapezoid:
            {
                var pts = trapezoid.GetLocalCorners().Select(c => new Point(c.X + o.X, c.Y + o.Y).Transform(m)).ToArray();
                DrawPolygon(context, CollisionFill, EditorPreferences.ShowPlatformOutline ? CollisionOutline : null, pts);
                return false;
            }
            case AreaElement area:
            {
                var r = ScreenRect(e, o, m);
                DrawBox(context, r, AreaFill, EditorPreferences.ShowOutline ? AreaOutline : null);
                bool animatedTrick = area.AreaType == "Trick" && DrawTrickVisuals(context, area, o, m, seconds);
                if (EditorPreferences.ShowAreaText)
                {
                    string text = !EditorPreferences.ShowAreaType ? area.Name
                        : area.AreaType == "Trick" && !string.IsNullOrEmpty(area.ItemName) ? $"{area.Name} [Trick: {area.ItemName}]" : $"{area.Name} [{area.AreaType}]";
                    DrawLabel(context, text, r, 11, LabelBrush, wrap: true);
                }
                return animatedTrick;
            }
            case TriggerElement trigger:
            {
                var r = ScreenRect(e, o, m);
                DrawBox(context, r, TriggerFill, EditorPreferences.ShowOutline ? TriggerOutline : null);
                if (EditorPreferences.ShowTriggerText)
                    DrawLabel(context, trigger.Name, r, 11, LabelBrush, wrap: true);
                return false;
            }
            case CommentElement comment:
            {
                var r = ScreenRect(e, o, m);
                DrawBox(context, r, CommentFill, CommentOutline);
                DrawLabel(context, comment.Name, r, 12, CommentText, wrap: true);
                return false;
            }
            case BuildingMarkerElement marker:
            {
                var center = new Point(o.X + marker.X, o.Y + marker.Y);
                DrawMarker(context, m, center, marker.IsIn ? Colors.LimeGreen : Colors.Red);
                var r = ScreenRect(e, o, m);
                DrawCaption(context, marker.IsIn ? "In" : "Out", new Point(r.Center.X, r.Bottom + 2), marker.IsIn ? Brushes.DarkGreen : Brushes.DarkRed);
                return false;
            }
            case SpawnElement spawn:
            {
                var r = ScreenRect(e, o, m);
                var mark = MarkBitmap;
                if (mark != null)
                {
                    var red = TintCache.Get(mark, Colors.Red) ?? mark;
                    context.DrawImage(red, new Rect(0, 0, red.PixelSize.Width, red.PixelSize.Height), r);
                }
                else
                {
                    var redPen = new Pen(Brushes.Red, 3);
                    context.DrawLine(redPen, r.TopLeft, r.BottomRight);
                    context.DrawLine(redPen, r.TopRight, r.BottomLeft);
                }
                DrawCaption(context, spawn.Name, new Point(r.Center.X, r.Bottom + 2), Brushes.DarkRed);
                return false;
            }
            case ItemElement it:
            {
                var asset = _editor.Context.Textures.TryGet(ItemTexture(it));
                var center = new Point(o.X + it.X, o.Y + it.Y);
                if (asset != null)
                {
                    var local = new Matrix(asset.Width, 0, 0, asset.Height, center.X - asset.Width / 2.0, center.Y - asset.Height / 2.0);
                    DrawAsset(context, asset, local * m, Colors.White, seconds);
                    return asset.IsAnimated;
                }
                var sc = center.Transform(m);
                double radius = 30 * m.M11;
                context.DrawEllipse(ItemFallback, new Pen(Brushes.DarkGoldenrod, 1), sc, radius, radius);
                return false;
            }
            case CameraElement camera:
            {
                var center = new Point(o.X + camera.X, o.Y + camera.Y);
                var view = CameraElement.ViewSize;
                var viewRect = new Rect(center.X - view.Width / 2, center.Y - view.Height / 2, view.Width, view.Height);
                var sr = new Rect(viewRect.TopLeft.Transform(m), viewRect.BottomRight.Transform(m));
                context.DrawRectangle(null, CameraPen, Snap(sr));
                var sc = center.Transform(m);
                DrawCameraIcon(context, sc);
                string mode = camera.Selection switch { ModeSelection.CommonMode => "Camera (Common)", ModeSelection.HunterMode => "Camera (Hunter)", _ => "Camera" };
                DrawCaption(context, mode, new Point(sc.X, sc.Y + 14), CameraIconBrush);
                return false;
            }
        }
        return false;
    }

    private static string ItemTexture(ItemElement item) => item.ItemType == ItemType.Coin ? "credits" : "bonus_v4";

    private static Rect ScreenRect(SceneElement e, Point o, Matrix m)
    {
        var a = new Point(o.X + e.X, o.Y + e.Y).Transform(m);
        Point b;
        if (e is PointElement pe)
        {
            var s = pe.HandleSize;
            a = new Point(o.X + e.X - s.Width / 2, o.Y + e.Y - s.Height / 2).Transform(m);
            b = new Point(o.X + e.X + s.Width / 2, o.Y + e.Y + s.Height / 2).Transform(m);
        }
        else
        {
            b = new Point(o.X + e.X + e.Width, o.Y + e.Y + e.Height).Transform(m);
        }
        return RectEx.Box(a, b);
    }

    private static Rect Snap(Rect r) => new(Math.Round(r.X) + 0.5, Math.Round(r.Y) + 0.5, Math.Round(r.Width), Math.Round(r.Height));

    private static void DrawBox(DrawingContext context, Rect r, IBrush fill, Pen? outline)
    {
        context.FillRectangle(fill, r);
        if (outline != null && r.Width >= 1 && r.Height >= 1)
            context.DrawRectangle(null, outline, Snap(r));
    }

    private static void DrawPolygon(DrawingContext context, IBrush? fill, Pen? pen, IReadOnlyList<Point> pts)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(pts[0], fill != null);
            for (int i = 1; i < pts.Count; i++)
                g.LineTo(pts[i]);
            g.EndFigure(true);
        }
        context.DrawGeometry(fill, pen, geometry);
    }

    private void DrawCameraIcon(DrawingContext context, Point center)
    {
        var icon = ElementTypeInfo.GetIcon(ElementType.Camera);
        var box = new Rect(center.X - 12, center.Y - 12, 24, 24);
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)), new Pen(CameraIconBrush, 1), center, 15, 15);
        if (icon != null)
        {
            var b = icon.Bounds;
            double s = 18 / Math.Max(b.Width, b.Height);
            using (context.PushTransform(Matrix.CreateTranslation(-b.Center.X, -b.Center.Y) * Matrix.CreateScale(s, s) * Matrix.CreateTranslation(box.Center.X, box.Center.Y)))
                context.DrawGeometry(CameraIconBrush, null, icon);
        }
    }

    private FormattedText GetText(string text, double size, IBrush brush, double maxWidth)
    {
        uint color = brush is ISolidColorBrush sb ? sb.Color.ToUInt32() : 0;
        var key = (text, size, color, Math.Round(maxWidth));
        if (_texts.TryGetValue(key, out var ft))
            return ft;
        if (_texts.Count > 4000)
            _texts.Clear();
        ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, size, brush);
        if (maxWidth > 0)
            ft.MaxTextWidth = maxWidth;
        _texts[key] = ft;
        return ft;
    }

    private void DrawLabel(DrawingContext context, string? text, Rect r, double size, IBrush brush, bool wrap = false)
    {
        if (string.IsNullOrEmpty(text) || r.Width < 14 || r.Height < 10)
            return;
        const double pad = 3;
        var inner = r.Deflate(pad);
        if (inner.Width <= 0 || inner.Height <= 0)
            return;
        // Wrapping widths are quantized so zooming does not lay the text out again every frame.
        var ft = GetText(text, size, brush, wrap ? Math.Max(8, Math.Floor(inner.Width / 6) * 6) : 0);

        var anchor = EditorPreferences.TextAnchor;
        int col = (int)anchor % 3, row = (int)anchor / 3;
        double x = col switch { 0 => inner.X, 1 => inner.X + (inner.Width - ft.Width) / 2, _ => inner.Right - ft.Width };
        double y = row switch { 0 => inner.Y, 1 => inner.Y + (inner.Height - ft.Height) / 2, _ => inner.Bottom - ft.Height };
        if (ft.Width > inner.Width) x = inner.X;
        using (context.PushClip(inner))
            context.DrawText(ft, new Point(x, y));
    }

    private void DrawCaption(DrawingContext context, string? text, Point topCenter, IBrush brush)
    {
        if (string.IsNullOrEmpty(text))
            return;
        var ft = GetText(text, 10, brush, 0);
        // Captions stay inside the decoration margin used for culling.
        double width = Math.Min(ft.Width, DecorationMarginPixels * 1.8);
        var area = new Rect(topCenter.X - width / 2, topCenter.Y, width, ft.Height);
        using (context.PushClip(area.Inflate(1)))
            context.DrawText(ft, area.TopLeft);
    }

    // ================= TRICK AREAS ================= //

    private readonly Dictionary<string, Bitmap?> _trackImages = new(StringComparer.Ordinal);

    /// <summary>
    /// How a Trick Area looks in the game (Unity AreaElement.CreateTrickVisuals): the animated trick_idle_up at the
    /// Area's center, and the trick's TRACK_ image (icons/tricks) 23.5 units below it, scaled 0.906 × 0.914.
    /// Returns whether something animated was drawn.
    /// </summary>
    private bool DrawTrickVisuals(DrawingContext context, AreaElement area, Point o, Matrix m, double seconds)
    {
        var b = area.GetLocalBounds();
        var center = new Point(o.X + b.Center.X, o.Y + b.Center.Y);
        bool animated = false;
        if (_editor.Context.Textures.TryGet("trick_idle_up") is { } idle)
        {
            var local = new Matrix(idle.Width, 0, 0, idle.Height, center.X - idle.Width / 2.0, center.Y - idle.Height / 2.0);
            DrawAsset(context, idle, local * m, Colors.White, seconds);
            animated = idle.IsAnimated;
        }
        if (string.IsNullOrEmpty(area.ItemName))
            return animated;
        string name = "TRACK_" + area.ItemName;
        if (!_trackImages.TryGetValue(name, out var track))
        {
            _trackImages[name] = null;
            _ = LoadTrackImageAsync(name);
            return animated;
        }
        if (track != null)
        {
            double w = track.PixelSize.Width * 0.9059364, h = track.PixelSize.Height * 0.9138624;
            var tc = new Point(center.X, center.Y + 23.5259);
            var a = new Point(tc.X - w / 2, tc.Y - h / 2).Transform(m);
            var c = new Point(tc.X + w / 2, tc.Y + h / 2).Transform(m);
            context.DrawImage(track, new Rect(track.Size), RectEx.Box(a, c));
        }
        return animated;
    }

    private async Task LoadTrackImageAsync(string name)
    {
        string folder = VPath.Combine(_editor.Project.Path, "icons", "tricks");
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
        {
            string path = VPath.Combine(folder, name + ext);
            try
            {
                if (!await AppServices.FileSystem.FileExistsAsync(path))
                    continue;
                _trackImages[name] = await ImageLoader.LoadAsync(AppServices.FileSystem, path);
                RedrawRequested?.Invoke();
            }
            catch
            {
                // shown without the trick image
            }
            return;
        }
    }

    /// <summary>The Spawn-style marker (Assets/Gizmos/mark.png) tinted, 100 level units, centered on a level point.</summary>
    public static void DrawMarker(DrawingContext context, Matrix levelToScreen, Point center, Color color)
    {
        var a = new Point(center.X - 50, center.Y - 50).Transform(levelToScreen);
        var b = new Point(center.X + 50, center.Y + 50).Transform(levelToScreen);
        var r = RectEx.Box(a, b);
        var mark = MarkBitmap;
        if (mark != null)
        {
            var tinted = TintCache.Get(mark, color) ?? mark;
            context.DrawImage(tinted, new Rect(0, 0, tinted.PixelSize.Width, tinted.PixelSize.Height), r);
        }
        else
        {
            var pen = new Pen(new SolidColorBrush(color), 3);
            context.DrawLine(pen, r.TopLeft, r.BottomRight);
            context.DrawLine(pen, r.TopRight, r.BottomLeft);
        }
    }

    private static Bitmap? MarkBitmap
    {
        get
        {
            if (_mark != null)
                return _mark;
            try
            {
                using var stream = AssetLoader.Open(new Uri("avares://Vectorier.Core/Assets/Gizmos/mark.png"));
                _mark = new Bitmap(stream);
            }
            catch
            {
                _mark = null;
            }
            return _mark;
        }
    }

    // ================= PLACEMENT PREVIEW ================= //

    /// <summary>
    /// Draws an element that is not part of the level (placement / drag preview), semi-transparent, on the
    /// gameplay layer. Its referenced content and children are drawn like the real thing.
    /// </summary>
    private RenderTargetBitmap? _ghostLayer;

    /// <param name="renderScaling">Pixels per unit of the screen (DPI scaling), for a sharp preview.</param>
    public void RenderGhost(DrawingContext context, SceneViewport viewport, SceneElement element, double opacity, double renderScaling = 1)
    {
        var group = new LayerGroup(Layers.GetFactor(Layers.Default), 0);
        var items = new List<RenderItem>();
        CollectGhost(element, element, group, null, default, items, 0);
        if (items.Count == 0)
            return;
        var m = viewport.LevelToScreen(group);
        double seconds = AnimationClock.Seconds;

        // The preview is drawn once into its own layer and that layer is faded: overlapping parts do not add up,
        // so the whole preview has one even transparency.
        Rect? union = null;
        foreach (var item in items)
        {
            var o = Origin(item);
            var b = item.Element.GetLocalBounds().Translate(new Vector(o.X, o.Y));
            var a = b.TopLeft.Transform(m);
            var c = b.BottomRight.Transform(m);
            var r = RectEx.Box(a, c).Inflate(24); // captions and outlines
            union = union == null ? r : union.Value.Union(r);
        }
        var area = union!.Value.Intersect(new Rect(viewport.Size).Inflate(2));
        if (area.Width < 1 || area.Height < 1)
            return;
        area = new Rect(Math.Floor(area.X), Math.Floor(area.Y), Math.Ceiling(area.Width) + 1, Math.Ceiling(area.Height) + 1);
        var pixels = new PixelSize(Math.Max(1, (int)Math.Ceiling(area.Width * renderScaling)), Math.Max(1, (int)Math.Ceiling(area.Height * renderScaling)));
        if (_ghostLayer == null || _ghostLayer.PixelSize != pixels)
        {
            _ghostLayer?.Dispose();
            _ghostLayer = new RenderTargetBitmap(pixels, new Vector(96 * renderScaling, 96 * renderScaling));
        }
        using (var layer = _ghostLayer.CreateDrawingContext(true))
        using (layer.PushTransform(Matrix.CreateTranslation(-area.X, -area.Y)))
        {
            foreach (var item in items.Where(i => !i.IsOverlay).OrderBy(i => DepthGroup(i.Element)).ThenBy(i => i.SortKey))
                DrawVisual(layer, item, m, seconds);
            foreach (var item in items.Where(i => i.IsOverlay).OrderBy(i => OverlayCategory(i.Element.Type)).ThenBy(i => i.SortKey))
                DrawOverlay(layer, item, m, viewport, seconds);
        }
        using (context.PushOpacity(opacity))
            context.DrawImage(_ghostLayer, new Rect(_ghostLayer.Size), area);
    }

    private static void CollectGhost(SceneElement element, SceneElement owner, LayerGroup group, ObjectElement? instance, Vector offset,
        List<RenderItem> items, int depth)
    {
        if (depth > 48)
            return;
        if (element is ObjectElement obj)
        {
            var inst = instance ?? obj;
            var inner = instance == null ? default : offset + new Vector(obj.X, obj.Y);
            if (obj.Reference is { } definition)
                foreach (var content in Ordered(definition.Content))
                    CollectGhost(content, owner, group, inst, inner, items, depth + 1);
            foreach (var child in Ordered(obj.Children))
                CollectGhost(child, owner, group, inst, inner, items, depth + 1);
            return;
        }
        items.Add(new RenderItem
        {
            Element = element,
            Owner = owner,
            Instance = ReferenceEquals(element, owner) ? null : instance,
            GhostOffset = offset,
            Group = group,
            IsOverlay = IsOverlayType(element.Type),
            SortKey = items.Count,
            Dirty = true, // never enters the spatial index
        });
    }

    // ================= SELECTION ================= //

    /// <summary>Green outlines of the selected elements (culled like everything else).</summary>
    public void RenderSelection(DrawingContext context, SceneViewport viewport, IReadOnlyList<SceneElement> selection)
    {
        var screen = new Rect(viewport.Size).Inflate(DecorationMarginPixels);
        foreach (var element in selection)
        {
            if (!_editor.IsTypeVisible(element.Type))
                continue;
            var group = GroupFor(element);
            var m = viewport.LevelToScreen(group);
            var o = element.ParentWorldPosition;

            Point[] corners;
            if (element is ObjectElement obj)
            {
                var b = obj.GetLocalBounds();
                if (obj.Children.Count == 0 && obj.Reference?.Bounds == null)
                    b = new Rect(obj.X - 20, obj.Y - 20, 40, 40);
                corners = new[] { b.TopLeft, b.TopRight, b.BottomRight, b.BottomLeft };
            }
            else if (element is ModelElement { SilhouetteBounds: null } || element is PointElement and not ModelElement)
            {
                corners = element.GetLocalCorners();
            }
            else
            {
                corners = element.GetLocalCorners();
            }

            var pts = corners.Select(c => new Point(c.X + o.X, c.Y + o.Y).Transform(m)).ToArray();
            double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
            if (!new Rect(minX, minY, maxX - minX, maxY - minY).Intersects(screen))
                continue;
            if (maxX - minX < 4 && maxY - minY < 4)
            {
                context.DrawEllipse(null, SelectionPen, new Point((minX + maxX) / 2, (minY + maxY) / 2), 5, 5);
                continue;
            }
            DrawPolygon(context, null, SelectionPen, pts);
        }
    }

    // ================= SNAPPING ================= //

    /// <summary>
    /// Screen corners (polygon order) of the visible items near a screen point, for transform snapping.
    /// <paramref name="exclude"/> drops items (the dragged selection).
    /// </summary>
    public void CollectSnapShapes(SceneViewport viewport, Point screen, double radiusPixels, bool platformsOnly,
        Func<RenderItem, bool> exclude, List<Point[]> output)
    {
        if (_needsRebuild)
            Rebuild();
        ProcessDirty();
        var candidates = new List<RenderItem>();
        int stamp = ++_stamp;
        foreach (var group in _groups)
        {
            var p = viewport.ScreenToLevel(screen, group);
            double r = radiusPixels / (viewport.Zoom * group.FrameScale);
            group.Grid.Query(new Rect(p.X - r, p.Y - r, r * 2, r * 2), stamp, candidates);
        }
        foreach (var item in candidates)
        {
            if (IsHidden(item) || item.Element is CameraElement or BuildingMarkerElement)
                continue;
            // C: Trapezoids count as Platforms.
            if (platformsOnly && item.Element.Type is not (ElementType.Platform or ElementType.Trapezoid))
                continue;
            if (exclude(item))
                continue;
            output.Add(ItemScreenCorners(viewport, item));
        }
    }

    /// <summary>Screen corners of the visible items drawn for an element and everything below it.</summary>
    public void CollectOwnShapes(SceneViewport viewport, SceneElement root, bool platformsOnly, List<Point[]> output)
    {
        if (_needsRebuild)
            Rebuild();
        ProcessDirty();

        Point Screen(Point level) => level.Transform(viewport.LevelToScreen(GroupFor(root)));
        var parentWorld = root.ParentWorldPosition;
        var pivot = new Point(parentWorld.X + root.X, parentWorld.Y + root.Y);
        switch (root)
        {
            // Building In / Out snap by their center.
            case BuildingMarkerElement:
                output.Add(new[] { Screen(pivot) });
                return;
            // Models snap by their own pivot (the point their position refers to); V also offers the bounds.
            case ModelElement:
                output.Add(new[] { Screen(pivot) });
                if (platformsOnly)
                    return;
                break;
            // C on a Building instance: its In and Out are the points that snap.
            case ObjectElement { Reference: { } def } when platformsOnly && (def.In != null || def.Out != null):
                foreach (var p in new[] { def.In, def.Out })
                    if (p is { } local)
                        output.Add(new[] { Screen(new Point(pivot.X + local.X, pivot.Y + local.Y)) });
                return;
        }
        foreach (var e in SceneDocument.Walk(root))
        {
            if (_byElement.TryGetValue(e, out var list))
                Add(list);
            if (_byInstance.TryGetValue(e, out var ghosts))
                Add(ghosts);
        }

        void Add(List<RenderItem> items)
        {
            foreach (var item in items)
            {
                if (IsHidden(item) || item.Element is CameraElement)
                    continue;
                if (platformsOnly && item.Element.Type is not (ElementType.Platform or ElementType.Trapezoid))
                    continue;
                output.Add(ItemScreenCorners(viewport, item));
            }
        }
    }

    private static Point[] ItemScreenCorners(SceneViewport viewport, RenderItem item)
    {
        var m = viewport.LevelToScreen(item.Group);
        var o = Origin(item);
        var corners = item.Element.GetLocalCorners();
        var result = new Point[corners.Length];
        for (int i = 0; i < corners.Length; i++)
            result[i] = new Point(corners[i].X + o.X, corners[i].Y + o.Y).Transform(m);
        return result;
    }

    /// <summary>Screen polygon of an element (used by tools for handles).</summary>
    public Point[] ScreenCorners(SceneViewport viewport, SceneElement element)
    {
        var m = viewport.LevelToScreen(GroupFor(element));
        var o = element.ParentWorldPosition;
        Point[] corners = element is ObjectElement obj
            ? new[] { obj.GetLocalBounds().TopLeft, obj.GetLocalBounds().TopRight, obj.GetLocalBounds().BottomRight, obj.GetLocalBounds().BottomLeft }
            : element.GetLocalCorners();
        return corners.Select(c => new Point(c.X + o.X, c.Y + o.Y).Transform(m)).ToArray();
    }

    public Rect ScreenBounds(SceneViewport viewport, IEnumerable<SceneElement> elements)
    {
        Rect? union = null;
        foreach (var e in elements)
        {
            var pts = ScreenCorners(viewport, e);
            var r = new Rect(new Point(pts.Min(p => p.X), pts.Min(p => p.Y)), new Point(pts.Max(p => p.X), pts.Max(p => p.Y)));
            union = union == null ? r : union.Value.Union(r);
        }
        return union ?? default;
    }
}
