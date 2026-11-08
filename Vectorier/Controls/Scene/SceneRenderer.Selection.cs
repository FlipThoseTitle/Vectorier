using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Vectorier.Models.Scene;

namespace Vectorier.Controls.Scene;

public partial class SceneRenderer
{
    /// <summary>Green outlines of the selected elements (culled like everything else).</summary>
    public void RenderSelection(DrawingContext context, SceneViewport viewport, IReadOnlyList<SceneElement> selection, Pen? pen = null)
    {
        pen ??= SelectionPen;
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
                context.DrawEllipse(null, pen, new Point((minX + maxX) / 2, (minY + maxY) / 2), 5, 5);
                continue;
            }
            DrawPolygon(context, null, pen, pts);
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
