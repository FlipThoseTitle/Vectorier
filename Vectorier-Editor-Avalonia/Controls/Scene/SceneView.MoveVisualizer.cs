using System;
using System.Numerics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Vectorier_Editor_Avalonia.Models.Scene;
using Vectorier_Editor_Avalonia.Services.MoveVisualizer;
using Vectorier_Editor_Avalonia.ViewModels.Editor;

namespace Vectorier_Editor_Avalonia.Controls.Scene;

/// <summary>
/// Move Visualizer drawing (Unity ModelRenderer + ModelDebug): the runner as a black silhouette between the
/// gameplay layer and the Overlay layer, then its nodes and bones, the center-of-mass path, the detector lines,
/// the placement preview and the platform-snap marker over everything. Only drawn when on screen.
/// </summary>
public partial class SceneView
{
    private static readonly IBrush MvSilhouette = new SolidColorBrush(Colors.Black);
    private static readonly IBrush MvGhost = new SolidColorBrush(Color.FromArgb(0x50, 0, 0, 0));
    private static readonly IBrush MvNode = new SolidColorBrush(Colors.Red);
    private static readonly IBrush MvNodePreview = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0, 0));
    private static readonly IBrush MvCamera = new SolidColorBrush(Colors.Yellow);
    private static readonly IBrush MvCameraPreview = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0));
    private static readonly Pen MvEdgeWithModel = new(new SolidColorBrush(Colors.Yellow), 1);
    private static readonly Pen MvEdgeNoModel = new(new SolidColorBrush(Color.FromRgb(0, 0xFF, 0)), 1);
    private static readonly Pen MvEdgePreview = new(new SolidColorBrush(Color.FromArgb(0x59, 0, 0xFF, 0)), 1);
    private static readonly Pen MvTrajectory = new(new SolidColorBrush(Colors.Red), 3, lineJoin: PenLineJoin.Round);
    private static readonly IBrush MvDetectorLine = new SolidColorBrush(Colors.Blue);
    private static readonly IBrush MvDetectorDot = new SolidColorBrush(Color.FromRgb(0, 0xFF, 0));
    private static readonly Pen MvSnapEdge = new(new SolidColorBrush(Colors.Cyan), 2);
    private static readonly Pen MvSnapCorner = new(new SolidColorBrush(Colors.Yellow), 2);

    private void OnMoveVisualizerChanged() => Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);

    /// <summary>Follow Camera: centers the view on a point of the character's layer.</summary>
    private void OnMoveVisualizerFollow(Point level)
    {
        if (_editor == null || _renderer == null) return;
        var host = _editor.MoveVisualizer.Host;
        double factor = host != null ? _renderer.GroupFor(host).Factor : Layers.GetFactor(Layers.Default);
        _center = factor > 1e-9 ? new Point(level.X / factor, level.Y / factor) : level;
        UpdateEditorCenter();
        InvalidateVisual();
    }

    private Matrix MoveVisualizerMatrix(SceneViewport viewport, SceneElement? host)
    {
        var group = host != null && _renderer != null
            ? _renderer.GroupFor(host)
            : new LayerGroup(Layers.GetFactor(Layers.Default), -1);
        return viewport.LevelToScreen(group);
    }

    /// <summary>
    /// The black silhouettes (placed character and placement ghost). Drawn by the renderer right after the
    /// gameplay layer, so the model sits above the level's gameplay art and below the Overlay layer and the
    /// layers in front of it (no z-fighting with gameplay art).
    /// </summary>
    private void RenderMoveVisualizerModel(DrawingContext context, SceneViewport viewport)
    {
        if (_editor == null) return;
        var mv = _editor.MoveVisualizer;
        var anim = mv.Animation;
        if (anim.Model is not { } model || !mv.RenderModel) return;
        var screen = new Rect(viewport.Size);

        if (mv.IsPlaced && anim.IsOffsetInitialized && anim.AnimationNodes is { } nodes)
        {
            var m = MoveVisualizerMatrix(viewport, mv.Host);
            double k = m.M11;
            var pts = Project(nodes, p => mv.ToLevel(p), m);
            if (ScreenBounds(pts, model.MaxRadius * k + 2).Intersects(screen))
                context.DrawGeometry(MvSilhouette, null, BuildSilhouette(model, nodes, pts, k));
        }

        if (mv.IsPlacementActive && anim.IsPreviewActive && anim.PreviewNodes is { } preview)
        {
            var m = MoveVisualizerMatrix(viewport, null);
            double k = m.M11;
            var pts = Project(preview, MoveVisualizerViewModel.PreviewToLevel, m);
            if (ScreenBounds(pts, model.MaxRadius * k + 2).Intersects(screen))
                context.DrawGeometry(MvGhost, null, BuildSilhouette(model, preview, pts, k));
        }
    }

    /// <summary>Debug drawing over everything (Unity Handles): trajectory, bones, nodes, detectors, preview nodes, snap.</summary>
    private void RenderMoveVisualizer(DrawingContext context, SceneViewport viewport)
    {
        if (_editor == null) return;
        var mv = _editor.MoveVisualizer;
        var anim = mv.Animation;
        var model = anim.Model;
        if (model == null) return;
        var screen = new Rect(viewport.Size);

        // ---- Placed animation ----
        if (mv.IsPlaced && anim.IsOffsetInitialized && anim.AnimationNodes is { } nodes)
        {
            var m = MoveVisualizerMatrix(viewport, mv.Host);
            double k = m.M11;
            var pts = Project(nodes, p => mv.ToLevel(p), m);
            bool visible = ScreenBounds(pts, model.MaxRadius * k + 8 * k + 8).Intersects(screen);

            if (mv.RenderTrajectory && anim.CenterOfMassPath.Count >= 2)
            {
                var path = new StreamGeometry();
                using (var g = path.Open())
                {
                    g.BeginFigure(mv.ToLevel(anim.CenterOfMassPath[0]).Transform(m), false);
                    for (int i = 1; i < anim.CenterOfMassPath.Count; i++)
                        g.LineTo(mv.ToLevel(anim.CenterOfMassPath[i]).Transform(m));
                    g.EndFigure(false);
                }
                if (path.Bounds.Inflate(3).Intersects(screen))
                    context.DrawGeometry(null, MvTrajectory, path);
            }

            if (visible)
            {
                if (mv.RenderEdges)
                {
                    DrawConnections(context, model, pts, mv.RenderModel ? MvEdgeWithModel : MvEdgeNoModel);
                    DrawNodes(context, model, pts, k, MvNode, MvCamera);
                }
                if (mv.RenderModel && mv.RenderDetector)
                    DrawDetectors(context, mv, model, pts, k);
            }
        }

        // ---- Placement preview ----
        if (mv.IsPlacementActive && anim.IsPreviewActive && anim.PreviewNodes is { } preview)
        {
            var m = MoveVisualizerMatrix(viewport, null);
            double k = m.M11;
            var pts = Project(preview, MoveVisualizerViewModel.PreviewToLevel, m);
            if (mv.RenderEdges)
                DrawConnections(context, model, pts, MvEdgePreview);
            DrawNodes(context, model, pts, k, MvNodePreview, MvCameraPreview);

            if (mv.SnapIndicator is { } snap)
            {
                var a = snap.Cursor.Transform(m);
                var b = snap.Target.Transform(m);
                var pen = snap.IsCorner ? MvSnapCorner : MvSnapEdge;
                context.DrawLine(pen, a, b);
                context.DrawEllipse(pen.Brush, null, b, 5, 5);
            }
        }
    }

    private static Point[] Project(Vector3[] nodes, Func<Vector3, Point> toLevel, Matrix m)
    {
        var pts = new Point[nodes.Length];
        for (int i = 0; i < nodes.Length; i++)
            pts[i] = toLevel(nodes[i]).Transform(m);
        return pts;
    }

    private static Rect ScreenBounds(Point[] pts, double inflate)
    {
        if (pts.Length == 0) return default;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in pts)
        {
            minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
        }
        return new Rect(minX - inflate, minY - inflate, maxX - minX + inflate * 2, maxY - minY + inflate * 2);
    }

    /// <summary>
    /// Unity ModelRenderer.UpdateCapsules seen from the front: spheres of Radius1 / Radius2 at the trimmed ends
    /// and a cylinder of their average radius between them.
    /// </summary>
    private static Geometry BuildSilhouette(MoveModel model, Vector3[] nodes, Point[] pts, double k)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.SetFillRule(FillRule.NonZero);
        foreach (var c in model.Capsules)
        {
            if ((uint)c.A >= (uint)nodes.Length || (uint)c.B >= (uint)nodes.Length)
                continue;
            float t0 = Math.Clamp(c.Margin1, 0, 1), t1 = Math.Clamp(1 - c.Margin2, 0, 1);
            if (t1 < t0) (t0, t1) = (t1, t0);
            // Hidden like Unity when the trimmed capsule has no length (in 3D).
            if ((Vector3.Lerp(nodes[c.A], nodes[c.B], t1) - Vector3.Lerp(nodes[c.A], nodes[c.B], t0)).Length() < 1e-4f)
                continue;

            var a = pts[c.A];
            var b = pts[c.B];
            var p0 = a + (b - a) * t0;
            var p1 = a + (b - a) * t1;
            double ra = Math.Max(0.0001, c.Radius1) * k;
            double rb = Math.Max(0.0001, c.Radius2) * k;
            double rc = Math.Max(0.0001, (c.Radius1 + c.Radius2) * 0.5) * k;

            AddCircle(ctx, p0, ra);
            AddCircle(ctx, p1, rb);
            var d = p1 - p0;
            double len = Math.Sqrt(d.X * d.X + d.Y * d.Y);
            if (len < 1e-6)
            {
                AddCircle(ctx, p0, rc); // pointing at the viewer: the cylinder is a disc
                continue;
            }
            var n = new Avalonia.Vector(-d.Y / len * rc, d.X / len * rc);
            Services.Assets.FillPolygon.Add(ctx, p0 + n, p1 + n, p1 - n, p0 - n);
        }
        return geometry;
    }

    private static void AddCircle(StreamGeometryContext ctx, Point c, double r)
    {
        if (r <= 0) return;
        ctx.BeginFigure(new Point(c.X + r, c.Y), true);
        ctx.ArcTo(new Point(c.X - r, c.Y), new Size(r, r), 0, false, SweepDirection.Clockwise);
        ctx.ArcTo(new Point(c.X + r, c.Y), new Size(r, r), 0, false, SweepDirection.Clockwise);
        ctx.EndFigure(true);
    }

    /// <summary>Bones (Muscle edges are skipped, as in Unity).</summary>
    private static void DrawConnections(DrawingContext context, MoveModel model, Point[] pts, Pen pen)
    {
        foreach (var e in model.Connections)
        {
            if (string.Equals(e.Type, "Muscle", StringComparison.OrdinalIgnoreCase)) continue;
            if ((uint)e.A >= (uint)pts.Length || (uint)e.B >= (uint)pts.Length) continue;
            context.DrawLine(pen, pts[e.A], pts[e.B]);
        }
    }

    /// <summary>Node spheres (2 units across; the Camera node 12).</summary>
    private static void DrawNodes(DrawingContext context, MoveModel model, Point[] pts, double k, IBrush normal, IBrush camera)
    {
        model.TryGetNodeIndex("Camera", out int cameraIndex);
        for (int i = 0; i < pts.Length; i++)
        {
            bool isCamera = i == cameraIndex;
            double r = Math.Max(isCamera ? 6 * k : 1 * k, 1.5);
            context.DrawEllipse(isCamera ? camera : normal, null, pts[i], r, r);
        }
    }

    /// <summary>DetectorH / DetectorV lines (blue, 2.5 units) with green end dots (8 units).</summary>
    private static void DrawDetectors(DrawingContext context, MoveVisualizerViewModel mv, MoveModel model, Point[] pts, double k)
    {
        var pen = new Pen(MvDetectorLine, Math.Max(1, 2.5 * k));
        double dot = Math.Max(2, 4 * k);
        void Draw(string node, int length, bool horizontal)
        {
            if (length <= 0 || !model.TryGetNodeIndex(node, out int index) || index >= pts.Length) return;
            double half = length * 0.5 * k;
            var c = pts[index];
            var a = horizontal ? new Point(c.X - half, c.Y) : new Point(c.X, c.Y + half);
            var b = horizontal ? new Point(c.X + half, c.Y) : new Point(c.X, c.Y - half);
            context.DrawLine(pen, a, b);
            context.DrawEllipse(MvDetectorDot, null, a, dot, dot);
            context.DrawEllipse(MvDetectorDot, null, b, dot, dot);
        }
        Draw("DetectorH", mv.DeltaDetectorH, true);
        Draw("DetectorV", mv.DeltaDetectorV, false);
    }
}
