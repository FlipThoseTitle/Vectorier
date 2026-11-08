using System;
using System.Collections.Generic;
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

    /// <summary>
    /// Live Collaboration: the same element on every participant's computer has the same SyncId (0 = not given one
    /// yet). Kept by undo snapshots, new for duplicates. Never written to files.
    /// </summary>
    public long SyncId { get; set; }

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

    /// <summary>Locked from the Hierarchy's lock: cannot be picked in the Scene (editor only, never written).</summary>
    [ObservableProperty] private bool _isLocked;

    /// <summary>This element or one of its parents is locked.</summary>
    public bool IsLockedInScene
    {
        get
        {
            for (var e = this; e != null; e = e.Parent)
                if (e.IsLocked)
                    return true;
            return false;
        }
    }

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

    /// <summary>Transformations the element runs that are not its own (an Object instance: its definition's).</summary>
    public virtual IReadOnlyList<DynamicTransformation> InheritedDynamics => Array.Empty<DynamicTransformation>();

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
        {
            copy.Id = Id;
            copy.SyncId = SyncId;
        }
        return copy;
    }

    protected abstract SceneElement CreateEmpty();

    /// <summary>
    /// Takes another element's properties (Live Collaboration applying someone's edit). The source has no children,
    /// so an Object keeps its own.
    /// </summary>
    public void CopyPropertiesFrom(SceneElement source) => source.CopyTo(this);

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

    public override string ToString() => $"{Type} '{DisplayName}'";
}
