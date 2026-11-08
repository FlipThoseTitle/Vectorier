using System;
using System.Collections.Generic;
using Avalonia;
using Vectorier.Models.Scene;

namespace Vectorier.Controls.Scene;

public partial class SceneRenderer
{
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
            if (IsHidden(item) || IsLocked(item))
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
            if (IsHidden(item) || IsLocked(item))
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
            // Rectangle selection only picks elements of the Default / Gameplay layer (factor 1).
            if (Math.Abs(group.Factor - GameplayFactor) > 1e-9)
                continue;
            var a = viewport.ScreenToLevel(screenRect.TopLeft, group);
            var b = viewport.ScreenToLevel(screenRect.BottomRight, group);
            group.Grid.Query(RectEx.Box(a, b), stamp, candidates);
        }
        foreach (var item in candidates)
        {
            if (IsHidden(item) || IsLocked(item))
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
}
