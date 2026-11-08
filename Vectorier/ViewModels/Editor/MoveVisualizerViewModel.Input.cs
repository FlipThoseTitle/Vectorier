using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia;
using Vectorier.Models.Scene;
using Vectorier.Services.MoveVisualizer;

namespace Vectorier.ViewModels.Editor;

public partial class MoveVisualizerViewModel
{
    /// <summary>Pointer moved over the Scene while placing: the preview pose follows (pivot at the cursor).</summary>
    public void UpdatePlacementPreview(Point level)
    {
        if (!IsPlacementActive || _model == null) return;
        Animation.EnsureModelLoadedForPreview(_model);
        var target = level;
        SnapIndicator = null;
        if (PlatformSnapHeld && TryGetPlatformSnapPoint(level, out var snapped, out bool corner))
        {
            SnapIndicator = new MoveSnapIndicator(level, snapped, corner);
            target = snapped;
        }
        if (IsGameplayMode)
        {
            UpdateGameplayPreview(target);
            Changed?.Invoke();
            return;
        }
        try
        {
            Animation.UpdatePreview(ToSpace(target, null), PivotNodeName);
        }
        catch (Exception ex)
        {
            Report(ex.Message);
        }
        Changed?.Invoke();
    }

    /// <summary>Left click while placing: the move starts with its pivot at the (snapped) cursor.</summary>
    public async Task PlaceFromSceneAsync(Point level)
    {
        if (!IsPlacementActive) return;
        var target = level;
        if (PlatformSnapHeld && TryGetPlatformSnapPoint(level, out var snapped, out _))
            target = snapped;
        if (IsGameplayMode)
        {
            // The spawn move is always JumpOff from frame 18 (|1: reversed), as the game spawns the player.
            string animation = WithReverse($"{GameplaySession.SpawnMove}|{GameplaySession.SpawnFrame}", Reverse);
            if (await SpawnGameplayAsync(new GameplaySpawn(new System.Numerics.Vector2((float)target.X, (float)target.Y), animation, PivotNodeName)))
            {
                GameplayPreviewNodes = null;
                Changed?.Invoke();
            }
            return;
        }
        if (await PlaceAtAsync(ToSpace(target, null), null))
        {
            Animation.ClearPreview();
            Changed?.Invoke();
        }
    }

    /// <summary>Unity TransformSnap.TryGetPlatformSnapPoint over the level's Platforms.</summary>
    private bool TryGetPlatformSnapPoint(Point point, out Point snapped, out bool isCorner)
    {
        snapped = point;
        isCorner = false;
        Point bestCorner = default, bestEdge = default;
        double bestCornerDist = double.MaxValue, bestEdgeDist = double.MaxValue;

        if (!_editor.IsTypeVisible(ElementType.Platform))
            return false;
        foreach (var r in PlatformRects())
        {
            double dx = Math.Max(Math.Max(r.Left - point.X, 0), point.X - r.Right);
            double dy = Math.Max(Math.Max(r.Top - point.Y, 0), point.Y - r.Bottom);
            if (dx * dx + dy * dy > BoundsSearchThreshold * BoundsSearchThreshold) continue;

            var corners = new[] { r.BottomLeft, r.BottomRight, r.TopLeft, r.TopRight };
            foreach (var c in corners)
            {
                double d = Distance(point, c);
                if (d < bestCornerDist) { bestCornerDist = d; bestCorner = c; }
            }
            foreach (var p in new[]
                     {
                         ClosestOnSegment(corners[0], corners[1], point), ClosestOnSegment(corners[1], corners[3], point),
                         ClosestOnSegment(corners[3], corners[2], point), ClosestOnSegment(corners[2], corners[0], point),
                     })
            {
                double d = Distance(point, p);
                if (d < bestEdgeDist) { bestEdgeDist = d; bestEdge = p; }
            }
        }

        if (bestCornerDist <= VertexSnapRadius)
        {
            snapped = bestCorner;
            isCorner = true;
            return true;
        }
        if (bestEdgeDist <= BoundsSearchThreshold)
        {
            snapped = bestEdge;
            return true;
        }
        return false;
    }

    /// <summary>Level bounds of every Platform, including those inside Objects' referenced content.</summary>
    private IEnumerable<Rect> PlatformRects()
    {
        var results = new List<Rect>();
        void Visit(SceneElement e, Avalonia.Vector parentWorld, int depth)
        {
            if (depth > 48) return;
            if (e is PlatformElement)
            {
                results.Add(e.GetLocalBounds().Translate(parentWorld));
                return;
            }
            if (e is not ObjectElement obj) return;
            var inner = parentWorld + new Avalonia.Vector(obj.X, obj.Y);
            if (obj.Reference is { } def)
            {
                _editor.BuildDefinition(def);
                foreach (var c in def.Content) Visit(c, inner, depth + 1);
            }
            foreach (var c in obj.Children) Visit(c, inner, depth + 1);
        }
        foreach (var root in _editor.Document.Roots)
            Visit(root, default, 0);
        return results;
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static Point ClosestOnSegment(Point a, Point b, Point p)
    {
        var ab = b - a;
        double len2 = ab.X * ab.X + ab.Y * ab.Y;
        if (len2 < 1e-9) return a;
        double t = Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / len2, 0, 1);
        return new Point(a.X + ab.X * t, a.Y + ab.Y * t);
    }

    // ================= COORDINATES ================= //

    /// <summary>Host position in Unity-like space (Y up).</summary>
    private static Vector3 HostOffset(SceneElement? host)
    {
        if (host == null) return Vector3.Zero;
        var p = host.WorldPosition;
        return new Vector3((float)p.X, (float)-p.Y, 0);
    }

    /// <summary>Level point (Y down) → animation space (Y up, relative to the host).</summary>
    public static Vector3 ToSpace(Point level, SceneElement? host) => new Vector3((float)level.X, (float)-level.Y, 0) - HostOffset(host);

    /// <summary>Animation-space point → level point, following the host if it has moved since.</summary>
    public Point ToLevel(Vector3 space)
    {
        var w = space + HostOffset(Host);
        return new Point(w.X, -w.Y);
    }

    /// <summary>The preview is always placed without a host.</summary>
    public static Point PreviewToLevel(Vector3 space) => new(space.X, -space.Y);
}
