#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Vectorier.Gameplay.Level;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Node;
using Vectorier.Gameplay.Rl;
using Vectorier.Gameplay.Runners;
using static Vectorier.Gameplay.MoveConditionsOverlay;

namespace Vectorier.Gameplay;

/// <summary>
/// The Moves Information modes other than Reaction Condition (I key): Physics, Safe and Platform Bound, and the
/// VelosityX / VelosityY readout they share with Reaction Condition. Each mode shows only what applies to the
/// player's playing move.
/// </summary>
public static class MovesInfoOverlays
{
    const float FontSize = 14;
    const float LineHeight = 17;

    static readonly Color Title = new(255, 200, 80, 255);
    static readonly Color Text = new(235, 235, 235, 255);
    static readonly Color Dim = new(165, 165, 170, 255);
    static readonly Color Yes = new(90, 220, 110, 255);
    static readonly Color No = new(240, 80, 80, 255);
    static readonly Color BadgeBack = new(15, 15, 18, 210);
    static readonly Color Trajectory = new(255, 60, 60, 255);
    static readonly Color BoundColor = new(0, 210, 255, 255);

    static ModelHuman? Player => LevelMainController.current?.Location?.GetUserModel();

    static Vector2 ToScreen(Camera2D camera, Vector3 p) =>
        Raylib.GetWorldToScreen2D(Sets.Current.Containers[1].TransformPoint(p), camera);

    static string F(double v) => MoveConditionsOverlay.F(v);

    static string OnOff(bool value) => value ? "1" : "0";

    // ================= VELOSITY ================= //

    /// <summary>
    /// VelosityX / VelosityY (the spelling of moves.xml): how far DetectorH and DetectorV moved since the previous
    /// update, per update, as absolute values. Fixed in the bottom-left corner, above the move name.
    /// </summary>
    public static void DrawVelosity()
    {
        var player = Player;
        var animations = player?.ControllerAnimations;
        if (player == null || animations?.Animation == null)
            return;
        var obj = player.ModelObject;
        float scale = animations.VelocityScale;
        Vector2 Of(ModelNode? node) => node == null ? Vector2.Zero
            : new Vector2(MathF.Abs(node.Start.X - node.End.X), MathF.Abs(node.Start.Y - node.End.Y)) * scale;
        var h = Of(obj.DetectorHorizontalNode);
        var v = Of(obj.DetectorVerticalNode);

        var lines = new List<(string, Color, float)>
        {
            ($"VelosityX - DetectorH: {F(h.X)}", Text, 0),
            ($"VelosityX - DetectorV: {F(v.X)}", Text, 0),
            ($"VelosityY - DetectorH: {F(h.Y)}", Text, 0),
            ($"VelosityY - DetectorV: {F(v.Y)}", Text, 0),
        };
        DrawPanelAt(lines, 10, Raylib.GetScreenHeight() - 40 - lines.Count * LineHeight);
    }

    static void DrawPanelAt(List<(string text, Color color, float indent)> lines, float x, float y)
    {
        const float pad = 6;
        float width = 0;
        foreach (var line in lines)
            width = MathF.Max(width, line.indent + TextWidth(line.text));
        Raylib.DrawRectangleRec(new Vectorier.Gameplay.Rl.Rectangle(x - pad, y - pad, width + pad * 2, lines.Count * LineHeight + pad * 2), new Color(15, 15, 18, 185));
        for (int i = 0; i < lines.Count; i++)
            GameFont.DrawUi(lines[i].text, x + lines[i].indent, y + i * LineHeight, FontSize, lines[i].color);
    }

    // ================= PHYSICS ================= //

    /// <summary>
    /// The playing move's Binding, Gravity, VelocityX and VelocityY (moves.xml attributes; a missing velocity is
    /// 0), the Velosity readout, and with Binding the COM's predicted trajectory: its position in every frame of
    /// the move as the game computed it when the move started (VelocityX / VelocityY moved each frame, Gravity
    /// added to VelocityY each frame, Y down).
    /// </summary>
    public static void DrawPhysics(Camera2D camera)
    {
        var player = Player;
        var animations = player?.ControllerAnimations;
        var move = animations?.Animation;
        if (player == null || move == null || Sets.Current == null)
            return;

        var lines = new List<(string, Color, float)>
        {
            ("Moves Information: Physics", Title, 0),
            ($"Move {move.Name}, frame {animations!.CurrentFrame}", Text, 0),
        };
        if (move.HasBinding || move.HasGravity)
        {
            lines.Add(($"Binding {(move.HasBinding ? OnOff(move.Binding) : "(not set)")}   Gravity {(move.HasGravity ? F(move.Gravity.Y) : "(not set)")}", Text, 0));
            lines.Add(($"VelocityX {F(move.Velocity.X)}{(move.HasVelocityX ? "" : " (not set: 0)")}   VelocityY {F(move.Velocity.Y)}{(move.HasVelocityY ? "" : " (not set: 0)")}", Text, 0));
            if (move.Binding)
            {
                // 20 animation frames per second; cm per frame → km/h.
                float speed = new Vector2(move.Velocity.X, move.Velocity.Y).Length() * 20f * 0.036f;
                lines.Add(($"Start speed {F(speed)} km/h ({F(new Vector2(move.Velocity.X, move.Velocity.Y).Length())} cm per frame, 20 FPS)", Dim, 0));
                lines.Add(("Red line: predicted COM trajectory", Trajectory, 0));
            }
        }
        else
        {
            lines.Add(("No Binding / Gravity on this move: no physics", Dim, 0));
        }
        MoveConditionsOverlay.DrawPanel(lines);
        DrawVelosity();

        var path = animations.PredictedPath;
        if (!move.Binding || path.Count < 2)
            return;
        var points = path.Select(p => ToScreen(camera, p)).ToList();
        for (int i = 1; i < points.Count; i++)
            Raylib.DrawLineEx(points[i - 1], points[i], 3, Trajectory);
        // A dot per animation frame shows the speed along the path.
        foreach (var p in points)
            Raylib.DrawCircleV(p, 3, Trajectory);
        Raylib.DrawCircleLinesV(points[0], 7, Trajectory);
        var com = player.ModelObject.CenterOfMassNode;
        if (com != null)
            Raylib.DrawCircleV(ToScreen(camera, com.Start), 4.5f, new Color(40, 120, 255, 255));
    }

    // ================= SAFE / LOCK ================= //

    /// <summary>
    /// Safe above NTop, SafeH right of DetectorH, SafeV right of DetectorV: a check when on, a cross when off. A
    /// padlock next to Safe shows Lock (the hunter cannot tase the model). The values are the ones the game uses
    /// now: a Safe / SafeH / SafeV written on the playing reaction overrides the interval's, each on its own.
    /// </summary>
    public static void DrawSafe(Camera2D camera)
    {
        var player = Player;
        var animations = player?.ControllerAnimations;
        var move = animations?.Animation;
        if (player == null || move == null || Sets.Current == null)
            return;
        var obj = player.ModelObject;
        var reaction = animations!.PlayingReaction;

        bool safe = animations.SafeInterval, safeH = animations.IsSafeH, safeV = animations.IsSafeV, locked = animations.LockInterval;

        string Source(bool? fromReaction, bool? fromInterval, string fallback) =>
            fromReaction.HasValue ? $"reaction {reaction!.Name}" : fromInterval.HasValue ? "interval" : fallback;

        var intervals = animations.CurrentIntervals;
        bool? intervalSafe = intervals.Any(i => i.IsSafe) ? true : null;
        var lines = new List<(string, Color, float)>
        {
            ("Moves Information: Safe", Title, 0),
            ($"Move {move.Name}, frame {animations.CurrentFrame}", Text, 0),
            ($"Safe {OnOff(safe)}   ({Source(reaction?.SafeAttribute, intervalSafe, "not set")})", safe ? Yes : No, 0),
            ($"SafeH {OnOff(safeH)}   ({Source(reaction?.SafeHAttribute, intervals.FirstOrDefault(i => i.SafeH.HasValue)?.SafeH, "not set")})", safeH ? Yes : No, 0),
            ($"SafeV {OnOff(safeV)}   ({Source(reaction?.SafeVAttribute, intervals.FirstOrDefault(i => i.SafeV.HasValue)?.SafeV, "not set")})", safeV ? Yes : No, 0),
            ($"Lock {OnOff(locked)}{(locked ? "   (the hunter cannot tase)" : "")}", locked ? Title : Dim, 0),
        };
        foreach (var interval in intervals.Where(i => i.IsSafe || i.IsLock || i.SafeH.HasValue || i.SafeV.HasValue))
        {
            var attrs = new List<string>();
            if (interval.IsSafe) attrs.Add("Safe=\"1\"");
            if (interval.SafeH.HasValue) attrs.Add($"SafeH=\"{OnOff(interval.SafeH.Value)}\"");
            if (interval.SafeV.HasValue) attrs.Add($"SafeV=\"{OnOff(interval.SafeV.Value)}\"");
            if (interval.IsLock) attrs.Add("Lock=\"1\"");
            lines.Add(($"Interval Start {interval.BeginFrame} End {interval.EndFrame}  {string.Join(" ", attrs)}", Dim, 10));
        }
        MoveConditionsOverlay.DrawPanel(lines);

        if (obj.DetectorVerticalNode != null)
            Badge(ToScreen(camera, obj.DetectorVerticalNode.Start) + new Vector2(12, -9), "SafeV", safeV);
        if (obj.DetectorHorizontalNode != null)
            Badge(ToScreen(camera, obj.DetectorHorizontalNode.Start) + new Vector2(12, -9), "SafeH", safeH);
        var top = obj.GetNode("NTop") ?? obj.CenterOfMassNode;
        if (top != null)
        {
            var at = ToScreen(camera, top.Start) + new Vector2(-9, -34);
            float w = Badge(at, "Safe", safe);
            LockBadge(at + new Vector2(w + 6, 0), locked);
        }
    }

    /// <summary>A check (on) or a cross (off) in a dark circle, with its name. Returns its width.</summary>
    static float Badge(Vector2 at, string name, bool on)
    {
        const float r = 9;
        var center = at + new Vector2(r, r);
        Raylib.DrawCircleV(center, r + 1, BadgeBack);
        var color = on ? Yes : No;
        if (on)
        {
            Raylib.DrawLineEx(center + new Vector2(-4.5f, 0), center + new Vector2(-1, 4), 2.5f, color);
            Raylib.DrawLineEx(center + new Vector2(-1, 4), center + new Vector2(5, -4), 2.5f, color);
        }
        else
        {
            Raylib.DrawLineEx(center + new Vector2(-4, -4), center + new Vector2(4, 4), 2.5f, color);
            Raylib.DrawLineEx(center + new Vector2(4, -4), center + new Vector2(-4, 4), 2.5f, color);
        }
        DrawOutlined(name, at + new Vector2(r * 2 + 4, 1), color, bold: true);
        return r * 2 + 4 + TextWidth(name);
    }

    /// <summary>A padlock: closed and yellow while Lock is on, open and grey otherwise.</summary>
    static void LockBadge(Vector2 at, bool locked)
    {
        var color = locked ? Title : Dim;
        var body = new Vectorier.Gameplay.Rl.Rectangle(at.X + 2, at.Y + 8, 14, 10);
        Raylib.DrawRectangleRec(new Vectorier.Gameplay.Rl.Rectangle(at.X, at.Y - 2, 18, 22), BadgeBack);
        // Shackle: a closed arch, or lifted to the side when open.
        float lift = locked ? 0 : -4;
        var c = new Vector2(at.X + 9, at.Y + 6 + lift);
        Raylib.DrawRing(c, 3, 5, 180, 360, 12, color);
        Raylib.DrawRectangleV(new Vector2(at.X + 4, c.Y), new Vector2(2, locked ? 3 : 2), color);
        if (locked)
            Raylib.DrawRectangleV(new Vector2(at.X + 12, c.Y), new Vector2(2, 3), color);
        Raylib.DrawRectangleRec(body, color);
        DrawOutlined("Lock", new Vector2(at.X + 22, at.Y + 1), color, bold: true);
    }

    // ================= PLATFORM BOUND ================= //

    /// <summary>
    /// The platform the model is bound to (it carries the model when it moves): outlined and named on the level,
    /// linked to the COM, with its movement. The list says whether the model is bound, which platform, how it
    /// moves, and the binding settings of the active intervals: NoPlatformBoundX / Y (the model stops following
    /// the platform on that axis) and ConditionlessPlatformBoundH / V / C (DetectorH, DetectorV or a collidable
    /// node binds to another platform without the reaction's conditions).
    /// </summary>
    public static void DrawPlatformBound(Camera2D camera)
    {
        var player = Player;
        var animations = player?.ControllerAnimations;
        var move = animations?.Animation;
        if (player == null || move == null || Sets.Current == null)
            return;

        var platform = animations!.BoundPlatform;
        var noBound = animations.NoPlatformBound;
        bool freeX = noBound.HasValue && noBound.Value.X > 0;
        bool freeY = noBound.HasValue && noBound.Value.Y > 0;

        var lines = new List<(string, Color, float)>
        {
            ("Moves Information: Platform Bound", Title, 0),
            ($"Move {move.Name}, frame {animations.CurrentFrame}", Text, 0),
        };
        if (platform == null || !platform.IsEnabled)
        {
            lines.Add(("Not bound to a platform", No, 0));
        }
        else
        {
            var tween = platform.TweenPosition;
            bool moving = tween.X != 0 || tween.Y != 0;
            lines.Add(($"Bound to {PlatformName(platform)}", BoundColor, 0));
            lines.Add(($"{(moving ? $"Moving {F(tween.X)}, {F(tween.Y)} per update" : "Not moving")}{(platform.Sticky ? "   Sticky" : "")}", Text, 10));
            lines.Add(($"X: {(freeX ? "not following the platform (NoPlatformBoundX)" : "follows the platform")}", freeX ? Dim : Text, 10));
            lines.Add(($"Y: {(freeY ? "not following the platform (NoPlatformBoundY)" : "follows the platform")}", freeY ? Dim : Text, 10));
        }

        foreach (var interval in animations.CurrentIntervals)
        {
            var attrs = new List<string>();
            if (interval.NoPlatformBound.X != 0) attrs.Add($"NoPlatformBoundX=\"{F(interval.NoPlatformBound.X)}\"");
            if (interval.NoPlatformBound.Y != 0) attrs.Add($"NoPlatformBoundY=\"{F(interval.NoPlatformBound.Y)}\"");
            if (interval.ConditionlessBoundH) attrs.Add("ConditionlessPlatformBoundH=\"1\" (DetectorH)");
            if (interval.ConditionlessBoundV) attrs.Add("ConditionlessPlatformBoundV=\"1\" (DetectorV)");
            if (interval.ConditionlessBoundC) attrs.Add("ConditionlessPlatformBoundC=\"1\" (collidable node)");
            if (attrs.Count > 0)
                lines.Add(($"Interval Start {interval.BeginFrame} End {interval.EndFrame}  {string.Join("  ", attrs)}", Dim, 0));
        }
        MoveConditionsOverlay.DrawPanel(lines);

        if (platform == null || !platform.IsEnabled)
            return;

        var corners = new[] { platform.Point1, platform.Point2, platform.Point3, platform.Point4 }.Select(p => ToScreen(camera, p)).ToArray();
        // Both windings: raylib only fills counter-clockwise triangles.
        foreach (var (a, b, c) in new[] { (0, 3, 2), (0, 2, 1), (0, 2, 3), (0, 1, 2) })
            Raylib.DrawTriangle(corners[a], corners[b], corners[c], Fade(BoundColor, 0.09f));
        for (int i = 0; i < 4; i++)
            Raylib.DrawLineEx(corners[i], corners[(i + 1) % 4], 3, BoundColor);

        // Link from the COM to the closest point of the platform's outline; the name goes just under it.
        var com = player.ModelObject.CenterOfMassNode;
        var anchor = corners[0];
        if (com != null)
        {
            var c = ToScreen(camera, com.Start);
            float best = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                var p = ClosestOnSegment(corners[i], corners[(i + 1) % 4], c);
                float d = Vector2.DistanceSquared(p, c);
                if (d < best)
                {
                    best = d;
                    anchor = p;
                }
            }
            Dashed(c, anchor, BoundColor);
            Raylib.DrawCircleV(c, 4, BoundColor);
            Raylib.DrawCircleV(anchor, 4, BoundColor);
        }
        var label = anchor + new Vector2(8, 10);
        label.X = Math.Clamp(label.X, 6, Raylib.GetScreenWidth() - 300);
        label.Y = Math.Clamp(label.Y, 6, Raylib.GetScreenHeight() - 60);
        DrawOutlined("Bound: " + PlatformName(platform), label, BoundColor, bold: true);
        if (freeX || freeY)
            DrawOutlined($"Not following on {(freeX && freeY ? "X and Y" : freeX ? "X" : "Y")}", label + new Vector2(0, LineHeight), Text, bold: false);

        // Movement arrow at the platform's center.
        var tw = platform.TweenPosition;
        if (tw.X != 0 || tw.Y != 0)
        {
            var center = new Vector2(corners.Average(p => p.X), corners.Average(p => p.Y));
            var dir = Vector2.Normalize(tw);
            var tip = center + dir * 40;
            Raylib.DrawLineEx(center, tip, 3, BoundColor);
            var side = new Vector2(-dir.Y, dir.X) * 6;
            Raylib.DrawTriangle(tip + dir * 8, tip + side, tip - side, BoundColor);
            Raylib.DrawTriangle(tip + dir * 8, tip - side, tip + side, BoundColor);
        }
    }

    static Vector2 ClosestOnSegment(Vector2 a, Vector2 b, Vector2 p)
    {
        var ab = b - a;
        float lengthSquared = ab.LengthSquared();
        if (lengthSquared < 1e-6f)
            return a;
        return a + ab * Math.Clamp(Vector2.Dot(p - a, ab) / lengthSquared, 0, 1);
    }

    static string PlatformName(QuadRunner platform) =>
        (string.IsNullOrEmpty(platform.Name) ? "unnamed platform" : platform.Name) +
        $" ({F(platform.Point1.X)}, {F(platform.Point1.Y)}, {F(platform.Width)} x {F(platform.Height)})";
}
