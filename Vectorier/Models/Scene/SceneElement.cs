using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Vectorier.Models.Scene;

/// <summary>The 13 element types supported by the editor, in Element-menu order.</summary>
public enum ElementType
{
    Object,
    Image,
    Trigger,
    Area,
    Platform,
    Trapezoid,
    Camera,
    Spawn,
    Model,
    Item,
    Animation,
    Particle,
    Comment,
    /// <summary>An XML element the editor does not understand; kept verbatim so saving never loses data.</summary>
    Unknown,
    /// <summary>Building entry point (InX / InY of a Building definition; Edit Object mode only).</summary>
    BuildingIn,
    /// <summary>Building exit point (OutX / OutY).</summary>
    BuildingOut,
}

/// <summary>Unity's Selection component: which game mode the element exists in.</summary>
public enum ModeSelection
{
    All,
    CommonMode,
    HunterMode
}

/// <summary>
/// Base class of every editor-owned scene object (the editor's equivalent of a Unity GameObject).
/// Positions are local to the parent Object and stored in Vector's convention (Y grows downward);
/// the Inspector shows Y negated so that Y grows upward.
/// </summary>
public abstract partial class SceneElement : ObservableObject
{
    private static int _nextId;

    protected SceneElement()
    {
        Id = Interlocked.Increment(ref _nextId);
    }

    /// <summary>Stable identity used by undo/redo and selection. Cloning keeps the id.</summary>
    public int Id { get; private set; }

    public abstract ElementType Type { get; }

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private double _width;
    [ObservableProperty] private double _height;

    /// <summary>Layer name (see <see cref="Layers"/>). Only meaningful on top-level elements; children inherit.</summary>
    [ObservableProperty] private string _layer = Layers.Default;

    /// <summary>Order in layer: sorts siblings when drawing and when writing XML.</summary>
    [ObservableProperty] private int _order;

    [ObservableProperty] private ModeSelection _selection = ModeSelection.All;

    // ---- Hierarchy UI state ----
    [ObservableProperty] private bool _isExpanded;

    /// <summary>Hidden in the Scene from the Hierarchy's eye (editor only, never written).</summary>
    [ObservableProperty] private bool _isHidden;

    /// <summary>This element or one of its parents is hidden.</summary>
    public bool IsHiddenInScene
    {
        get
        {
            for (var e = this; e != null; e = e.Parent)
                if (e.IsHidden)
                    return true;
            return false;
        }
    }
    [ObservableProperty] private bool _isSelected;

    public ObjectElement? Parent { get; internal set; }

    /// <summary>Dynamic &lt;Transformation&gt;s attached to this element.</summary>
    public List<DynamicTransformation> Dynamics { get; set; } = new();

    /// <summary>Attributes found in the XML that the editor does not model. Written back unchanged.</summary>
    public Dictionary<string, string> ExtraAttributes { get; set; } = new();

    /// <summary>Unknown children of &lt;Properties&gt;&lt;Static&gt; (kept verbatim).</summary>
    public List<XElement> ExtraStatic { get; set; } = new();

    /// <summary>Unknown children of &lt;Properties&gt; other than Static and Dynamic (kept verbatim).</summary>
    public List<XElement> ExtraProperties { get; set; } = new();

    /// <summary>Unknown direct children of the element (kept verbatim).</summary>
    public List<XElement> ExtraChildren { get; set; } = new();

    /// <summary>False when the XML had no X/Y (object definitions); the position is then omitted again while it stays at 0,0.</summary>
    public bool PositionWasPresent { get; set; } = true;

    // ---- Capabilities used by the tools and the Inspector ----
    public virtual bool HasSize => true;
    public virtual bool CanRotate => false;
    public virtual bool CanResize => HasSize;
    public virtual bool IntegerTransform => false;
    public virtual bool LayerEditable => true;
    public virtual bool NameEditable => true;
    public virtual bool SupportsDynamics => true;

    public string DisplayName => string.IsNullOrEmpty(Name) ? Type.ToString() : Name;

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DisplayName));

    /// <summary>The top-level ancestor (the element itself when it has no parent).</summary>
    public SceneElement Root
    {
        get
        {
            SceneElement current = this;
            while (current.Parent != null)
                current = current.Parent;
            return current;
        }
    }

    /// <summary>Layer that actually applies (inherited from the top-level ancestor).</summary>
    public string EffectiveLayer => Root.Layer;

    public double EffectiveFactor => Layers.GetFactor(EffectiveLayer);

    /// <summary>Position in level space (sum of local positions up the hierarchy), without parallax.</summary>
    public Point WorldPosition
    {
        get
        {
            double x = X, y = Y;
            var p = Parent;
            while (p != null)
            {
                x += p.X;
                y += p.Y;
                p = p.Parent;
            }
            return new Point(x, y);
        }
    }

    public Point ParentWorldPosition => Parent?.WorldPosition ?? new Point(0, 0);

    public bool IsDescendantOf(SceneElement other)
    {
        var p = Parent;
        while (p != null)
        {
            if (ReferenceEquals(p, other))
                return true;
            p = p.Parent;
        }
        return false;
    }

    /// <summary>Local-space corner points (clockwise from the pivot). Used for selection, hit testing and bounds.</summary>
    public virtual Point[] GetLocalCorners()
    {
        return new[]
        {
            new Point(X, Y),
            new Point(X + Width, Y),
            new Point(X + Width, Y + Height),
            new Point(X, Y + Height),
        };
    }

    /// <summary>Axis-aligned local bounds.</summary>
    public virtual Rect GetLocalBounds()
    {
        var corners = GetLocalCorners();
        double minX = corners.Min(c => c.X), minY = corners.Min(c => c.Y);
        double maxX = corners.Max(c => c.X), maxY = corners.Max(c => c.Y);
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>Scales the element around a local-space pivot (used by the Scale and Rect tools and by Object scaling).</summary>
    public virtual void ScaleAround(Point pivot, double sx, double sy)
    {
        X = pivot.X + (X - pivot.X) * sx;
        Y = pivot.Y + (Y - pivot.Y) * sy;
        if (HasSize)
        {
            Width *= Math.Abs(sx);
            Height *= Math.Abs(sy);
        }
    }

    /// <summary>Rotates the element's position around a local-space pivot; elements that can rotate also turn.</summary>
    public virtual void RotateAround(Point pivot, double radians)
    {
        double cos = Math.Cos(radians), sin = Math.Sin(radians);
        double dx = X - pivot.X, dy = Y - pivot.Y;
        X = pivot.X + dx * cos - dy * sin;
        Y = pivot.Y + dx * sin + dy * cos;
    }

    /// <summary>Snaps transform values to integers for types that require it (Platform, Trapezoid).</summary>
    public void ApplyIntegerLock()
    {
        if (!IntegerTransform)
            return;
        X = Math.Round(X);
        Y = Math.Round(Y);
        Width = Math.Max(1, Math.Round(Width));
        Height = Math.Max(1, Math.Round(Height));
    }

    // ---- Cloning (undo/redo, copy/paste, duplicate) ----

    /// <summary>Deep copy. When <paramref name="keepIds"/> is false the copy (and its children) receive new ids.</summary>
    public SceneElement Clone(bool keepIds)
    {
        var copy = CreateEmpty();
        CopyTo(copy);
        if (keepIds)
            copy.Id = Id;
        return copy;
    }

    protected abstract SceneElement CreateEmpty();

    protected virtual void CopyTo(SceneElement target)
    {
        target.Name = Name;
        target.X = X;
        target.Y = Y;
        target.Width = Width;
        target.Height = Height;
        target.Layer = Layer;
        target.Order = Order;
        target.Selection = Selection;
        target.IsExpanded = IsExpanded;
        target.Dynamics = Dynamics.Select(d => d.Clone()).ToList();
        target.ExtraAttributes = new Dictionary<string, string>(ExtraAttributes);
        target.ExtraStatic = ExtraStatic.Select(e => new XElement(e)).ToList();
        target.ExtraProperties = ExtraProperties.Select(e => new XElement(e)).ToList();
        target.ExtraChildren = ExtraChildren.Select(e => new XElement(e)).ToList();
        target.PositionWasPresent = PositionWasPresent;
    }

    internal void ReassignIds(bool keep)
    {
        if (!keep)
            Id = Interlocked.Increment(ref _nextId);
    }

    public override string ToString() => $"{Type} '{DisplayName}'";
}

/// <summary>
/// &lt;Object&gt;: groups other elements. When it has a Name, the game also draws the definition with that
/// name from the files listed in &lt;Sets&gt;; that resolved content is shown in the Scene but never exported.
/// </summary>
public sealed partial class ObjectElement : SceneElement
{
    public override ElementType Type => ElementType.Object;
    public override bool HasSize => false;
    public override bool CanResize => true; // scaling an Object scales its inline children

    public ObservableCollection<SceneElement> Children { get; } = new();

    /// <summary>
    /// Content resolved from &lt;Sets&gt; for a named Object (read-only prototype shared between instances).
    /// Null when the name does not match a definition.
    /// </summary>
    [ObservableProperty] private ObjectDefinition? _reference;

    public bool IsReference => Reference != null;

    /// <summary>Size of the MISSING box drawn for an Object whose definition cannot be found.</summary>
    public const double MissingSize = 100;

    /// <summary>A named Object with no definition and no content: its definition is missing (deleted, renamed...).</summary>
    public bool IsMissingDefinition => Reference == null && Children.Count == 0 && !string.IsNullOrEmpty(Name);

    partial void OnReferenceChanged(ObjectDefinition? value) => OnPropertyChanged(nameof(IsReference));

    public void AddChild(SceneElement child, int index = -1)
    {
        child.Parent?.Children.Remove(child);
        child.Parent = this;
        if (index < 0 || index > Children.Count)
            Children.Add(child);
        else
            Children.Insert(index, child);
    }

    public bool RemoveChild(SceneElement child)
    {
        if (!Children.Remove(child))
            return false;
        child.Parent = null;
        return true;
    }

    public override Point[] GetLocalCorners()
    {
        var bounds = GetLocalBounds();
        return new[] { bounds.TopLeft, bounds.TopRight, bounds.BottomRight, bounds.BottomLeft };
    }

    /// <summary>Bounds of inline children and resolved reference content, in the parent's space.</summary>
    public override Rect GetLocalBounds()
    {
        Rect? union = null;
        foreach (var child in Children)
        {
            var b = child.GetLocalBounds();
            union = union == null ? b : union.Value.Union(b);
        }
        if (Reference != null && Reference.Bounds is { } refBounds)
            union = union == null ? refBounds : union.Value.Union(refBounds);

        var content = union ?? (IsMissingDefinition ? new Rect(0, 0, MissingSize, MissingSize) : new Rect(0, 0, 0, 0));
        return content.Translate(new Vector(X, Y));
    }

    public override void ScaleAround(Point pivot, double sx, double sy)
    {
        // Objects have no size of their own: scale the children around the pivot expressed in local space.
        var localPivot = new Point(pivot.X - X, pivot.Y - Y);
        // Surface tiles are regenerated from their scaled Platform instead of being stretched.
        HashSet<int>? surfaced = null;
        foreach (var child in Children)
            if (child is PlatformElement { Surface: not null })
                (surfaced ??= new()).Add(child.Id);
        foreach (var child in Children)
        {
            if (surfaced != null && child is ImageElement { SurfaceOwner: not 0 } tile && surfaced.Contains(tile.SurfaceOwner))
                continue;
            child.ScaleAround(localPivot, sx, sy);
        }
        if (Children.Count == 0 && Reference == null)
            return;
        base.ScaleAround(pivot, 1, 1);
    }

    public override void RotateAround(Point pivot, double radians)
    {
        var localPivot = new Point(pivot.X - X, pivot.Y - Y);
        foreach (var child in Children)
            child.RotateAround(localPivot, radians);
    }

    protected override SceneElement CreateEmpty() => new ObjectElement();

    protected override void CopyTo(SceneElement target)
    {
        base.CopyTo(target);
        var obj = (ObjectElement)target;
        obj.Reference = Reference;
        Dictionary<int, int>? platformIds = null;
        foreach (var child in Children)
        {
            var childCopy = child.Clone(keepIds: false);
            obj.AddChild(childCopy);
            if (child is PlatformElement)
                (platformIds ??= new())[child.Id] = childCopy.Id;
        }
        // Surface tiles follow their (re-numbered) Platform.
        foreach (var image in obj.Children.OfType<ImageElement>())
        {
            if (image.SurfaceOwner != 0)
                image.SurfaceOwner = platformIds != null && platformIds.TryGetValue(image.SurfaceOwner, out var id) ? id : 0;
        }
    }

    /// <summary>Deep clone that keeps every id (used by undo snapshots).</summary>
    public static SceneElement CloneKeepingIds(SceneElement source)
    {
        var copy = source.Clone(keepIds: true);
        if (source is ObjectElement srcObj && copy is ObjectElement dstObj)
        {
            // Children were cloned with new ids; redo them keeping ids.
            dstObj.Children.Clear();
            foreach (var child in srcObj.Children)
                dstObj.AddChild(CloneKeepingIds(child));
        }
        return copy;
    }
}
