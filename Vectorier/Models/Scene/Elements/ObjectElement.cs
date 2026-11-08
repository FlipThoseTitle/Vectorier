using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Vectorier.Models.Scene;

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

    public override IReadOnlyList<DynamicTransformation> InheritedDynamics =>
        (IReadOnlyList<DynamicTransformation>?)Reference?.Dynamics ?? Array.Empty<DynamicTransformation>();

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
