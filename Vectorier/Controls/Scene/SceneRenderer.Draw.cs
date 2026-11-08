using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Vectorier.Models.Scene;
using Vectorier.Services;
using Vectorier.Services.Assets;
using Vectorier.Services.Localization;
using Vectorier.ViewModels.Editor;

namespace Vectorier.Controls.Scene;

public partial class SceneRenderer
{
    /// <summary>Highest parallax factor drawn before <c>afterGameplay</c> (the gameplay layer is 1; Overlay is 1.00001).</summary>
    public const double GameplayFactor = 1.0;

    /// <param name="afterGameplay">
    /// Drawn after the visuals of the gameplay layer (and every layer behind it) and before the Overlay layer and
    /// the layers in front of it (the Move Visualizer's silhouette).
    /// </param>
    /// <summary>The green level-origin marker (hidden during the tutorial, where it only confuses).</summary>
    public bool ShowOriginMarker { get; set; } = true;

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
        if (mode == SceneRenderMode.Editor && ShowOriginMarker)
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

    /// <summary>Device pixels per screen unit of the view being drawn (Windows display scaling...).</summary>
    public double RenderScaling { get; set; } = 1;

    private bool DrawImage(DrawingContext context, RenderItem item, ImageElement image, Point o, Matrix m, double seconds)
    {
        var textures = _editor.Context.Textures;
        var entry = textures.Find(image.ClassName);
        if (entry == null)
        {
            if (textures.IsMissing(image.ClassName))
                DrawMissing(context, image.GetLocalCorners(), o, m);
            return false;
        }
        // The size comes from the file's header for static textures: zoomed out, the full texture is never decoded.
        if (textures.TryGetSize(entry) is not { } size)
            return false;

        if (image.AutoSize && (Math.Abs(image.Width - size.Width) > 1e-6 || Math.Abs(image.Height - size.Height) > 1e-6))
        {
            // The game uses the texture size when the XML has none.
            using (_editor.SuppressTracking())
                image.FillNaturalSize(size.Width, size.Height);
            MarkDirty(item);
            if (item.Instance?.Reference is { } def)
                def.ComputeBounds();
            RedrawRequested?.Invoke();
        }

        var (a, b, c, d) = image.GetBasis();
        var local = new Matrix(a, b, c, d, o.X + image.X, o.Y + image.Y);
        return DrawTexture(context, entry, size.Width, local * m, image.Color, seconds);
    }

    /// <summary>
    /// Draws a texture on the unit square at the resolution the zoom needs: when it covers well under half of its
    /// pixels on screen, a downscaled copy (a power of two wide) is drawn instead of the full texture, which then
    /// does not even have to be loaded. Sprite sheets always use the full sheet (their frames are in pixels).
    /// Returns true when the texture animates.
    /// </summary>
    private bool DrawTexture(DrawingContext context, Services.Assets.TextureEntry entry, int pixelWidth, Matrix unitToScreen, Color color, double seconds)
    {
        var textures = _editor.Context.Textures;
        if (!entry.IsAnimated)
        {
            double screenWidth = Math.Sqrt(unitToScreen.M11 * unitToScreen.M11 + unitToScreen.M12 * unitToScreen.M12) * RenderScaling;
            int level = Services.Assets.TextureLibrary.ThumbnailWidth(screenWidth);
            if (level * 2 <= pixelWidth)
            {
                var loaded = textures.TryGetLoaded(entry.Name);
                var mip = textures.TryGetThumbnail(entry, level, nearest: true);
                if (mip != null)
                    DrawBitmap(context, mip, unitToScreen, color);
                else if (loaded != null)
                    DrawAsset(context, loaded, unitToScreen, color, seconds); // full texture meanwhile
                return false;
            }
        }
        var asset = textures.TryGet(entry.Name);
        if (asset == null)
        {
            // Zooming in on a downscaled texture: keep showing it until the full one is decoded.
            if (!entry.IsAnimated && textures.TryGetThumbnail(entry, Services.Assets.TextureLibrary.MaxThumbnailWidth, nearest: true) is { } any)
                DrawBitmap(context, any, unitToScreen, color);
            return false;
        }
        DrawAsset(context, asset, unitToScreen, color, seconds);
        return asset.IsAnimated;
    }

    /// <summary>A whole bitmap mapped onto the unit square (downscaled textures), tinted like <see cref="DrawAsset"/>.</summary>
    private static void DrawBitmap(DrawingContext context, Bitmap bitmap, Matrix unitToScreen, Color color)
    {
        if (color.A == 0)
            return;
        var source = TintCache.NeedsTint(color) ? TintCache.Get(bitmap, color) ?? bitmap : bitmap;
        using (context.PushTransform(unitToScreen))
        {
            var src = new Rect(0, 0, source.PixelSize.Width, source.PixelSize.Height);
            if (color.A < 255)
            {
                using (context.PushOpacity(color.A / 255.0))
                    context.DrawImage(source, src, new Rect(0, 0, 1, 1));
            }
            else
            {
                context.DrawImage(source, src, new Rect(0, 0, 1, 1));
            }
        }
    }

    private bool DrawTexturedRect(DrawingContext context, RenderItem item, SceneElement e, Point o, Matrix m, double seconds)
    {
        var textures = _editor.Context.Textures;
        var entry = textures.Find(e.Name);
        if (entry == null)
        {
            if (textures.IsMissing(e.Name))
                DrawMissing(context, e.GetLocalCorners(), o, m);
            return false;
        }
        if (textures.TryGetSize(entry) is not { } size)
            return false;
        var local = new Matrix(e.Width, 0, 0, e.Height, o.X + e.X, o.Y + e.Y);
        return DrawTexture(context, entry, size.Width, local * m, Colors.White, seconds);
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
    private LabelText? _missingText;
    private static readonly IBrush MissingBackground = new SolidColorBrush(Colors.White, 0.75);

    /// <summary>Missing texture: red outline and cross with a bold "Missing" scaled to the image's size.</summary>
    private void DrawMissing(DrawingContext context, Point[] corners, Point o, Matrix m)
    {
        var pts = ToScreen(corners, o, m);
        DrawPolygon(context, null, MissingPen, pts);
        if (pts.Length == 4)
        {
            context.DrawLine(MissingPen, pts[0], pts[2]);
            context.DrawLine(MissingPen, pts[1], pts[3]);
        }

        double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
        double w = maxX - minX, h = maxY - minY;
        // Reference text at size 100, scaled to fit 70 % of the width and 40 % of the height (padding around it).
        _missingText ??= new LabelText(new FormattedText(Loc.T("Missing"), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MissingTypeface, 100, MissingTextBrush));
        double scale = Math.Min(w * 0.7 / _missingText.Width, h * 0.4 / _missingText.Height);
        if (scale * 100 < 7)
            return;
        var center = new Point((minX + maxX) / 2, (minY + maxY) / 2);
        var bg = new Rect(center.X - _missingText.Width * scale / 2 - 4 * scale * 10, center.Y - _missingText.Height * scale / 2,
            _missingText.Width * scale + 8 * scale * 10, _missingText.Height * scale);
        context.FillRectangle(MissingBackground, bg);
        using (context.PushTransform(Matrix.CreateTranslation(-_missingText.Width / 2, -_missingText.Height / 2) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(center.X, center.Y)))
            _missingText.Draw(context, new Point(0, 0));
    }

    private static readonly IPen ModelPendingPen = new ImmutablePen(ModelBrush.ToImmutable(), 1, new ImmutableDashStyle(new double[] { 3, 3 }, 0));

    private void DrawModel(DrawingContext context, RenderItem item, ModelElement model, Point o, Matrix m)
    {
        var entry = GetModel(model.ClassName);
        if (entry.Geometry == null)
        {
            // Not loaded yet, unreadable, or skipped: a marker at the pivot.
            var center = new Point(o.X + model.X, o.Y + model.Y).Transform(m);
            context.DrawEllipse(null, ModelPendingPen, center, 10, 10);
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
                geometry = data.BuildSilhouette(out bounds);
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
                    var pts = ToScreen(trapezoid.GetLocalCorners(), o, m);
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
                    DrawCaption(context, marker.IsIn ? Loc.T("In") : Loc.T("Out"), new Point(r.Center.X, r.Bottom + 2), marker.IsIn ? Brushes.DarkGreen : Brushes.DarkRed);
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
                    string mode = camera.Selection switch { ModeSelection.CommonMode => Loc.T("Camera (Common)"), ModeSelection.HunterMode => Loc.T("Camera (Hunter)"), _ => Loc.T("Camera") };
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

    /// <summary>Local corners offset by <paramref name="o"/> and taken to the screen.</summary>
    private static Point[] ToScreen(Point[] corners, Point o, Matrix m)
    {
        var result = new Point[corners.Length];
        for (int i = 0; i < corners.Length; i++)
            result[i] = new Point(corners[i].X + o.X, corners[i].Y + o.Y).Transform(m);
        return result;
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

    /// <summary>
    /// A label laid out once and recorded: replaying the recording draws the same glyphs without laying the text out
    /// again (a FormattedText is laid out on every DrawText call).
    /// </summary>
    private sealed class LabelText
    {
        public LabelText(FormattedText text)
        {
            Width = text.Width;
            Height = text.Height;
            using (var context = Drawing.Open())
                context.DrawText(text, new Point(0, 0));
        }

        public double Width { get; }
        public double Height { get; }
        private DrawingGroup Drawing { get; } = new();

        public void Draw(DrawingContext context, Point origin)
        {
            using (context.PushTransform(Matrix.CreateTranslation(origin.X, origin.Y)))
                Drawing.Draw(context);
        }
    }

    private LabelText GetText(string text, double size, IBrush brush, double maxWidth)
    {
        uint color = brush is ISolidColorBrush sb ? sb.Color.ToUInt32() : 0;
        var key = (text, size, color, Math.Round(maxWidth));
        if (_texts.TryGetValue(key, out var label))
            return label;
        if (_texts.Count > 4000)
            _texts.Clear();
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, size, brush);
        if (maxWidth > 0)
            formatted.MaxTextWidth = maxWidth;
        label = new LabelText(formatted);
        _texts[key] = label;
        return label;
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
            ft.Draw(context, new Point(x, y));
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
            ft.Draw(context, area.TopLeft);
    }
}
