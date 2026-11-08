using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Vectorier.Models.Scene;

namespace Vectorier.Controls.Scene;

public partial class SceneRenderer
{
    /// <summary>
    /// Draws an element that is not part of the level (placement / drag preview), semi-transparent, on the
    /// gameplay layer. Its referenced content and children are drawn like the real thing.
    /// </summary>
    /// <summary>One offscreen layer per ghost drawn in a frame (the placement ghost, each onion-skin pose...).</summary>
    private readonly Dictionary<int, RenderTargetBitmap> _ghostLayers = new();

    /// <summary>The placement preview's picture (see RenderGhost's cached).</summary>
    private sealed record GhostCache(SceneElement Element, RenderTargetBitmap Bitmap, Rect Area, Point Anchor, double Scaling,
        double M11, double M12, double M21, double M22, double Seconds, double Since, bool Animated)
    {
        /// <summary>
        /// Drawn again a few times while its textures may still be loading (the first two seconds), then only
        /// when it moves (animations, particles), at most twice a second.
        /// </summary>
        public bool NeedsRefresh(double now) => (Animated || now - Since < 2) && now - Seconds >= 0.5;
    }
    private GhostCache? _ghostCache;

    /// <summary>The biggest cached preview picture (pixels).</summary>
    private const double GhostCacheMaxPixels = 4096.0 * 4096.0;

    /// <summary>The placement preview changed (another asset, or it was removed): its picture is drawn again.</summary>
    public void ForgetGhost()
    {
        _ghostCache?.Bitmap.Dispose();
        _ghostCache = null;
    }

    /// <param name="renderScaling">Pixels per unit of the screen (DPI scaling), for a sharp preview.</param>
    /// <param name="group">Parallax group to draw in (the Default layer when null).</param>
    /// <param name="offset">Level offset added to the element (the world position of the parent it stands for).</param>
    /// <param name="slot">Separate offscreen layer for each ghost drawn in the same frame.</param>
    /// <param name="cached">
    /// Draw once into a bitmap and move the bitmap with the pointer (redrawn on zoom changes and twice a second for
    /// loading textures and animations). Previews too large for one bitmap are drawn every frame instead.
    /// </param>
    public void RenderGhost(DrawingContext context, SceneViewport viewport, SceneElement element, double opacity, double renderScaling = 1,
        LayerGroup? group = null, Vector offset = default, int slot = 0, bool cached = false)
    {
        var layerGroup = group ?? new LayerGroup(Layers.GetFactor(Layers.Default), 0);
        var m = viewport.LevelToScreen(layerGroup);
        if (offset != default)
            m = Matrix.CreateTranslation(offset.X, offset.Y) * m;
        double seconds = AnimationClock.Seconds;
        // Where the element's own origin is on the screen: the cached picture follows it.
        var anchor = new Point(element.X, element.Y).Transform(m);
        if (cached && _ghostCache is { } hit && ReferenceEquals(hit.Element, element) && hit.Scaling == renderScaling
            && hit.M11 == m.M11 && hit.M12 == m.M12 && hit.M21 == m.M21 && hit.M22 == m.M22 && !hit.NeedsRefresh(seconds))
        {
            var moved = hit.Area.Translate(anchor - hit.Anchor);
            using (context.PushOpacity(opacity))
                context.DrawImage(hit.Bitmap, new Rect(0, 0, hit.Bitmap.PixelSize.Width, hit.Bitmap.PixelSize.Height), moved);
            return;
        }

        var items = new List<RenderItem>();
        CollectGhost(element, element, layerGroup, null, default, items, 0);
        if (items.Count == 0)
            return;

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
        // Cached: the whole preview, when it fits in one picture (else clipped to the view, drawn every frame).
        bool whole = cached && union!.Value.Width * renderScaling * union.Value.Height * renderScaling <= GhostCacheMaxPixels;
        var area = whole ? union!.Value : union!.Value.Intersect(new Rect(viewport.Size).Inflate(2));
        if (area.Width < 1 || area.Height < 1)
            return;
        area = new Rect(Math.Floor(area.X), Math.Floor(area.Y), Math.Ceiling(area.Width) + 1, Math.Ceiling(area.Height) + 1);
        var pixels = new PixelSize(Math.Max(1, (int)Math.Ceiling(area.Width * renderScaling)), Math.Max(1, (int)Math.Ceiling(area.Height * renderScaling)));
        RenderTargetBitmap ghostLayer;
        if (whole)
        {
            // Its own picture (the per-slot layers are reused every frame by the others).
            if (_ghostCache is { } old && old.Bitmap.PixelSize == pixels)
                ghostLayer = old.Bitmap;
            else
            {
                _ghostCache?.Bitmap.Dispose();
                _ghostCache = null;
                ghostLayer = new RenderTargetBitmap(pixels, new Vector(96 * renderScaling, 96 * renderScaling));
            }
            bool animated = items.Any(i => i.Element is AnimationElement or ParticleElement);
            double since = _ghostCache is { } same && ReferenceEquals(same.Element, element) ? same.Since : seconds;
            _ghostCache = new GhostCache(element, ghostLayer, area, anchor, renderScaling, m.M11, m.M12, m.M21, m.M22, seconds, since, animated);
        }
        else if (!_ghostLayers.TryGetValue(slot, out ghostLayer!) || ghostLayer.PixelSize != pixels)
        {
            ghostLayer?.Dispose();
            ghostLayer = new RenderTargetBitmap(pixels, new Vector(96 * renderScaling, 96 * renderScaling));
            _ghostLayers[slot] = ghostLayer;
        }
        using (var layer = ghostLayer.CreateDrawingContext(true))
        // Labels go into a transparent layer: sub-pixel (ClearType) text needs an opaque background and comes out
        // as colored, blotchy fringes there; grayscale antialiasing stays clean once the layer is faded.
        using (layer.PushRenderOptions(new RenderOptions { EdgeMode = EdgeMode.Antialias }))
        using (layer.PushTextOptions(new TextOptions { TextRenderingMode = TextRenderingMode.Antialias }))
        using (layer.PushTransform(Matrix.CreateTranslation(-area.X, -area.Y)))
        {
            foreach (var item in items.Where(i => !i.IsOverlay).OrderBy(i => DepthGroup(i.Element)).ThenBy(i => i.SortKey))
                DrawVisual(layer, item, m, seconds);
            foreach (var item in items.Where(i => i.IsOverlay).OrderBy(i => OverlayCategory(i.Element.Type)).ThenBy(i => i.SortKey))
                DrawOverlay(layer, item, m, viewport, seconds);
        }
        using (context.PushOpacity(opacity))
            // The source rectangle is in pixels: with display scaling above 100% the layer has more pixels than
            // its size in screen units, and taking only Size of it blew the preview up (it looked bigger than
            // what is placed).
            context.DrawImage(ghostLayer, new Rect(0, 0, ghostLayer.PixelSize.Width, ghostLayer.PixelSize.Height), area);
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
}
