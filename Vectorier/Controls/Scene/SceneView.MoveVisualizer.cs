using System;
using System.Numerics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Vectorier.Models.Scene;
using Vectorier.Services.MoveVisualizer;
using Vectorier.ViewModels.Editor;

namespace Vectorier.Controls.Scene;

/// <summary>
/// Move Visualizer drawing (Unity ModelRenderer + ModelDebug): the runner as a black silhouette between the
/// gameplay layer and the Overlay layer (the placed move or the gameplay character), then the center-of-mass path,
/// the placement preview and the platform-snap marker over everything. Only drawn when on screen.
/// </summary>
public partial class SceneView
{
    private static readonly IBrush MvSilhouette = new SolidColorBrush(Colors.Black);
    private static readonly IBrush MvGhost = new SolidColorBrush(Color.FromArgb(0x50, 0, 0, 0));
    private static readonly IBrush MvNodePreview = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0, 0));
    private static readonly IBrush MvCameraPreview = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0));
    private static readonly Pen MvTrajectory = new(new SolidColorBrush(Colors.Red), 3, lineJoin: PenLineJoin.Round);
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
    /// The black silhouettes (placed character, gameplay character and placement ghost), drawn by the game's model
    /// drawing (runner.xml). Drawn by the renderer right after the gameplay layer, so the model sits above the level's
    /// gameplay art and below the Overlay layer and the layers in front of it (no z-fighting with gameplay art).
    /// </summary>
    private void RenderMoveVisualizerModel(DrawingContext context, SceneViewport viewport)
    {
        if (_editor == null) return;
        var mv = _editor.MoveVisualizer;
        if (mv.Animation.Model is null) return;
        var screen = new Rect(viewport.Size);

        // Animation mode: the move, in the level coordinates of its host's layer.
        if (mv.PlacedCharacter is { } placed)
            DrawModel(context, placed.Object, MoveVisualizerMatrix(viewport, mv.Host), screen, MvSilhouette);

        // Gameplay: the character (level coordinates, gameplay layer).
        if (mv.GameplayCharacter is { } gameplay)
            DrawModel(context, gameplay, MoveVisualizerMatrix(viewport, null), screen, MvSilhouette);

        if (mv.IsPlacementActive && PreviewNodes(mv) is { } preview && mv.GhostAt(LevelNodes(preview)) is { } ghost)
            DrawModel(context, ghost.Object, MoveVisualizerMatrix(viewport, null), screen, MvGhost);
    }

    /// <summary>A model in level coordinates, through the layer's level → screen matrix (skipped when off screen).</summary>
    private static void DrawModel(DrawingContext context, Vectorier.Gameplay.Models.ModelObject model, Matrix m, Rect screen, IBrush brush)
    {
        var geometry = Services.Assets.ModelView.Draw(model, p => new Point(p.X, p.Y).Transform(m), m.M11, out var bounds);
        if (bounds.Inflate(2).Intersects(screen))
            context.DrawGeometry(brush, null, geometry);
    }

    /// <summary>The preview's nodes as the game's (level coordinates, Z as in the files).</summary>
    private static Vector3[] LevelNodes((Vector3[] Nodes, Func<Vector3, Point> ToLevel) preview)
    {
        var nodes = new Vector3[preview.Nodes.Length];
        bool gameplay = preview.ToLevel == (Func<Vector3, Point>)LevelPoint;
        for (int i = 0; i < nodes.Length; i++)
        {
            var p = preview.ToLevel(preview.Nodes[i]);
            nodes[i] = new Vector3((float)p.X, (float)p.Y, gameplay ? preview.Nodes[i].Z : -preview.Nodes[i].Z);
        }
        return nodes;
    }

    private static Point LevelPoint(Vector3 p) => new(p.X, p.Y);

    /// <summary>The placement preview: the move's start pose (Animation) or the runner's rest pose (Gameplay).</summary>
    private static (Vector3[] Nodes, Func<Vector3, Point> ToLevel)? PreviewNodes(MoveVisualizerViewModel mv)
    {
        if (mv.IsGameplayMode)
            return mv.GameplayPreviewNodes is { } nodes ? (nodes, LevelPoint) : null;
        return mv.Animation.IsPreviewActive && mv.Animation.PreviewNodes is { } preview ? (preview, MoveVisualizerViewModel.PreviewToLevel) : null;
    }

    /// <summary>Debug drawing over everything (Unity Handles): trajectory, preview nodes, snap.</summary>
    private void RenderMoveVisualizer(DrawingContext context, SceneViewport viewport)
    {
        if (_editor == null) return;
        var mv = _editor.MoveVisualizer;
        var anim = mv.Animation;
        var model = anim.Model;
        if (model == null) return;
        var screen = new Rect(viewport.Size);

        // ---- Placed animation ----
        if (mv.IsPlaced && anim.IsOffsetInitialized && anim.AnimationNodes is { } && mv.IsAnimationMode)
        {
            var m = MoveVisualizerMatrix(viewport, mv.Host);

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
        }

        // ---- Placement preview ----
        if (mv.IsPlacementActive && PreviewNodes(mv) is { } preview)
        {
            var m = MoveVisualizerMatrix(viewport, null);
            double k = m.M11;
            var pts = Project(preview.Nodes, preview.ToLevel, m);
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

}
