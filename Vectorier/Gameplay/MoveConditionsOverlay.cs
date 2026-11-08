#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Vectorier.Gameplay.Animation;
using Vectorier.Gameplay.Level;
using Vectorier.Gameplay.Rl;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay;

/// <summary>
/// Moves Information (I key), "Reaction Condition" mode. Shows, for the move playing now:
/// 1. the active intervals that use EventGroups (Start / End / Group);
/// 2. the event that started the move (Keys, Detector, OnCollision, OnTrigger, OnFrame, OnEnd) and its interval;
/// 3. the playing reaction, its attributes and ReactionGroup;
/// 4. its Reaction Conditions (Width, Height, DeltaX/Y, VelocityX/Y), the measurable ones drawn on the collision
///    they were measured on (a line from the condition's corner, mirrored when facing left, labelled "Min 200",
///    "Max 0" or "Range 200-400"; lines sharing a corner are set side by side).
/// The event and reaction come from ReactionTrace, recorded by ControllerAnimations.
/// </summary>
public static class MoveConditionsOverlay
{
    const float FontSize = 14;
    const float LineHeight = 17;
    const float LineGap = 16;      // screen pixels between two lines at the same corner
    const float FirstOffset = 6;   // first line, from the platform's edge
    const float Thick = 3;
    const float EndRadius = 4.5f;

    static readonly Color Title = new(255, 200, 80, 255);
    static readonly Color Text = new(235, 235, 235, 255);
    static readonly Color Dim = new(165, 165, 170, 255);
    static readonly Color PanelBack = new(15, 15, 18, 185);
    static readonly Color Shadow = new(0, 0, 0, 230);

    /// <summary>One color per condition, in the order the reaction lists them.</summary>
    static readonly Color[] Palette =
    {
        new(0, 205, 255, 255),   // cyan
        new(255, 120, 60, 255),  // orange
        new(120, 235, 90, 255),  // green
        new(255, 95, 200, 255),  // pink
        new(255, 225, 60, 255),  // yellow
        new(175, 140, 255, 255), // violet
    };

    static readonly List<Vectorier.Gameplay.Rl.Rectangle> placed = new();

    public static void Draw(Camera2D camera)
    {
        var player = LevelMainController.current?.Location?.GetUserModel();
        var animations = player?.ControllerAnimations;
        if (player == null || animations?.Animation == null || Sets.Current == null)
            return;

        placed.Clear();
        var vs = Sets.Current.Containers[1];
        Vector2 ToScreen(Vector3 p) => Raylib.GetWorldToScreen2D(vs.TransformPoint(p), camera);

        var lines = new List<(string text, Color color, float indent)>
        {
            ("Moves Information: Reaction Condition", Title, 0),
            ($"Move {animations.Animation.Name}, frame {animations.CurrentFrame}", Text, 0),
        };

        // 1. Active intervals that use EventGroups.
        foreach (var interval in animations.CurrentIntervals)
        {
            if (!string.IsNullOrEmpty(interval.Groups))
                lines.Add(($"Interval  Start {interval.BeginFrame}  End {interval.EndFrame}  Group {interval.Groups}", Text, 0));
        }

        // 2-4. The event and reaction that started the playing move.
        var trace = animations.LastReaction;
        var conditions = new List<(AnimationDelta delta, char type, Color color)>();
        if (trace != null && trace.Reaction.Name == animations.Animation.Name)
        {
            lines.Add((string.Empty, Text, 0));
            if (trace.Interval != null)
            {
                string groups = string.IsNullOrEmpty(trace.Interval.Groups) ? "" : $"  Group {trace.Interval.Groups}";
                lines.Add(($"Interval  Start {trace.Interval.BeginFrame}  End {trace.Interval.EndFrame}{groups}   (in {trace.FromMove}, frame {trace.FromFrame})", Text, 0));
            }
            else
            {
                lines.Add(($"In {trace.FromMove}, frame {trace.FromFrame}", Text, 0));
            }
            lines.Add(("Event  " + EventText(trace), Title, 10));

            var reaction = trace.Reaction;
            string reactionGroup = reaction.ReactionGroup is string rg ? $"   (ReactionGroup {rg})" : "";
            lines.Add(($"Reaction  {reaction.Name}{reactionGroup}", Title, 20));
            if (reaction.XmlAttributes.Length > 0)
                lines.Add((reaction.XmlAttributes, Dim, 30));

            // Conditions of the same kind on the same corner all have to hold, so they are one condition:
            // <DeltaY Max="0" Corner="0"/> + <DeltaY Min="-15" Corner="0"/> = <DeltaY Min="-15" Max="0" Corner="0"/>.
            // Each XML line is listed (in the merged condition's color) and the merged one is drawn once.
            int index = 0;
            foreach (var (deltas, type) in new[] { (reaction.DeltasH, 'H'), (reaction.DeltasV, 'V'), (reaction.DeltasC, 'C') })
            {
                foreach (var same in deltas.Items.GroupBy(d => (d.Name, d.Corner)))
                {
                    var color = Palette[index++ % Palette.Length];
                    var parts = same.ToList();
                    foreach (var delta in parts)
                        lines.Add((ConditionXml(delta, type) + ValueText(trace, delta, type), color, 30));
                    var merged = parts.Count == 1 ? parts[0] : Merge(parts);
                    if (parts.Count > 1)
                        lines.Add(($"= {ConditionXml(merged, type)}   combined: {Measurement(merged)}", color, 44));
                    conditions.Add((merged, type, color));
                }
            }
            if (index == 0)
                lines.Add(("No Reaction Conditions", Dim, 30));
        }

        DrawPanel(lines);
        if (trace != null && conditions.Count > 0)
            DrawConditions(trace, conditions, ToScreen);
    }

    // ================= TEXT ================= //

    internal static string F(double v) => Math.Round(v, 1).ToString(CultureInfo.InvariantCulture);

    static string EventText(ReactionTrace trace)
    {
        var param = trace.Event?.Param;
        if (param == null)
        {
            return trace.EventType switch
            {
                AnimationEventType.Controller => "Keys",
                AnimationEventType.DetectorOn => "Detector On",
                AnimationEventType.DetectorOff => "Detector Off",
                _ => trace.EventType.ToString(),
            };
        }
        string text = param.XmlName + (param.XmlAttributes.Length > 0 ? "  " + param.XmlAttributes : "");
        if (param.ReactionGroups != null)
            text += $"  Groups=\"{param.ReactionGroups}\"";
        if (param.EventGroup != null)
            text += $"   (EventGroup {param.EventGroup})";
        return text;
    }

    static string ConditionName(AnimationDelta delta) => delta.XmlName.Length > 0 ? delta.XmlName : delta.Name.ToString();

    static string ConditionXml(AnimationDelta delta, char type)
    {
        var parts = new List<string> { "<" + ConditionName(delta) };
        if (!float.IsNaN(delta.Value.X))
            parts.Add($"Min=\"{F(delta.Value.X)}\"");
        if (!float.IsNaN(delta.Value.Y))
            parts.Add($"Max=\"{F(delta.Value.Y)}\"");
        parts.Add($"Type=\"{type}\"");
        if (delta.Corner >= 0)
            parts.Add($"Corner=\"{delta.Corner}\"");
        return string.Join(" ", parts) + "/>";
    }

    static QuadRunner? PlatformOf(ReactionTrace trace, char type) => type switch
    {
        'H' => trace.PlatformH,
        'V' => trace.PlatformV,
        _ => trace.PlatformC,
    };

    /// <summary>The value measured when the reaction was chosen.</summary>
    static string ValueText(ReactionTrace trace, AnimationDelta delta, char type)
    {
        if (delta.Name is AnimationDeltaName.VelocityX or AnimationDeltaName.VelocityY)
            return $"   = {F(delta.Name == AnimationDeltaName.VelocityX ? trace.Velocity.X : trace.Velocity.Y)}";
        var platform = PlatformOf(trace, type);
        if (platform == null)
            return "   (no collision)";
        var position = type switch { 'H' => trace.PositionH, 'V' => trace.PositionV, _ => trace.PositionC };
        var data = new AnimationDeltaData(platform, position, trace.Sign);
        double value = data.GetDeltaValue(delta.Name, CornerOf(delta, trace.Sign), trace.Sign, trace.Velocity);
        return $"   = {F(value)}";
    }

    /// <summary>
    /// One condition from several of the same kind on the same corner: the highest Min and the lowest Max (all of
    /// them have to hold).
    /// </summary>
    static AnimationDelta Merge(List<AnimationDelta> parts)
    {
        float min = float.NaN, max = float.NaN;
        foreach (var d in parts)
        {
            if (!float.IsNaN(d.Value.X))
                min = float.IsNaN(min) ? d.Value.X : MathF.Max(min, d.Value.X);
            if (!float.IsNaN(d.Value.Y))
                max = float.IsNaN(max) ? d.Value.Y : MathF.Min(max, d.Value.Y);
        }
        var first = parts[0];
        return new AnimationDelta(first.Name, first.Type, new Vector2(min, max), first.Corner) { XmlName = first.XmlName };
    }

    static string Measurement(AnimationDelta delta)
    {
        float min = delta.Value.X, max = delta.Value.Y;
        if (!float.IsNaN(min) && !float.IsNaN(max) && min > max)
            return $"Never valid: Min {F(min)} > Max {F(max)}";
        if (!float.IsNaN(min) && !float.IsNaN(max))
            return min < 0 || max < 0 ? $"Range {F(min)} to {F(max)}" : $"Range {F(min)}-{F(max)}";
        if (!float.IsNaN(min))
            return $"Min {F(min)}";
        if (!float.IsNaN(max))
            return $"Max {F(max)}";
        return "Any";
    }

    // ================= ON THE COLLISION ================= //

    /// <summary>The corner used: the condition's, mirrored when facing left (no Corner: the front top corner).</summary>
    static int CornerOf(AnimationDelta delta, int sign) => delta.Corner < 0 ? (sign == 1 ? 0 : 1) : delta.GetCorner(sign);

    static bool Vertical(AnimationDeltaName name) => name is AnimationDeltaName.Height or AnimationDeltaName.DeltaY;

    static void DrawConditions(ReactionTrace trace, List<(AnimationDelta delta, char type, Color color)> conditions, Func<Vector3, Vector2> toScreen)
    {
        int sign = trace.Sign;

        // Lines at the same corner of the same collision along the same axis are set side by side.
        var groups = conditions
            .Where(c => c.delta.Name is AnimationDeltaName.Width or AnimationDeltaName.Height or AnimationDeltaName.DeltaX or AnimationDeltaName.DeltaY)
            .Where(c => PlatformOf(trace, c.type) != null)
            .GroupBy(c => (platform: PlatformOf(trace, c.type)!, corner: CornerOf(c.delta, sign), vertical: Vertical(c.delta.Name)));

        foreach (var group in groups)
        {
            var (platform, corner, vertical) = group.Key;
            var items = group.ToList();
            Vector3 c = platform.GetCornerByIndex(corner);
            // Into the collision from this corner (Y grows downward in Vector: negative Y is up).
            float inX = corner is 0 or 3 ? 1 : -1;
            float inY = corner is 0 or 1 ? 1 : -1;

            var cornerScreen = toScreen(c);
            Raylib.DrawCircleLinesV(cornerScreen, 6, Text);
            Raylib.DrawCircleV(cornerScreen, 2.5f, Text);

            // Labels go inside the collision, past all the lines of this corner.
            float labelOffset = FirstOffset + items.Count * LineGap + 2;

            for (int k = 0; k < items.Count; k++)
            {
                var (delta, _, color) = items[k];
                float offset = FirstOffset + k * LineGap;
                Vector2 shift = vertical ? new Vector2(inX * offset, 0) : new Vector2(0, inY * offset);

                // Direction of a positive value along the axis.
                float dir = delta.Name switch
                {
                    AnimationDeltaName.Height => inY,  // measured into the collision
                    AnimationDeltaName.Width => inX,
                    AnimationDeltaName.DeltaX => sign, // toward the facing direction
                    _ => 1,                            // DeltaY: positive is downward
                };
                Vector2 At(float v) => toScreen(vertical ? new Vector3(c.X, c.Y + dir * v, 0) : new Vector3(c.X + dir * v, c.Y, 0)) + shift;

                float min = delta.Value.X, max = delta.Value.Y;
                Vector2 a, b;
                if (!float.IsNaN(min) && !float.IsNaN(max))
                {
                    // Range: a dashed guide from the corner to Min, the valid part solid.
                    if (min != 0)
                        Dashed(At(0), At(min), color);
                    a = At(min);
                    b = At(max);
                }
                else
                {
                    a = At(0);
                    b = At(!float.IsNaN(min) ? min : !float.IsNaN(max) ? max : 0);
                }
                // Short link from the corner to the line set off the edge.
                Raylib.DrawLineEx(cornerScreen, At(0), 1, Fade(color, 0.6f));
                Raylib.DrawLineEx(a, b, Thick, color);
                Raylib.DrawCircleV(a, EndRadius, color);
                Raylib.DrawCircleV(b, EndRadius, color);

                string name = ConditionName(delta);
                string measurement = Measurement(delta);
                if (vertical)
                {
                    // Name at the top of the line, measurement beside its middle, both inside the collision.
                    float topY = MathF.Min(MathF.Min(a.Y, b.Y), At(0).Y);
                    float x = At(0).X - shift.X + inX * labelOffset;
                    PlaceBlock(name, measurement, new Vector2(x, topY - 2), inX < 0, color);
                }
                else
                {
                    // Name at the start of the line, measurement under its middle (above for a bottom corner).
                    float y = At(0).Y - shift.Y + inY * labelOffset + (inY < 0 ? -LineHeight * 2 : 0);
                    float left = MathF.Min(MathF.Min(a.X, b.X), At(0).X);
                    PlaceBlock(name, measurement, new Vector2(left, y), false, color);
                }
            }
        }
    }

    // ================= DRAWING HELPERS ================= //

    internal static Color Fade(Color c, float alpha) => new(c.R, c.G, c.B, (byte)Math.Clamp(alpha * 255, 0, 255));

    internal static float TextWidth(string text) => GameFont.UiWidth(text, FontSize);

    /// <summary>
    /// The condition's name (bold, in its color) with its measurement under it (white), kept together: when the
    /// block would cover one already drawn, the whole block moves down.
    /// </summary>
    static void PlaceBlock(string name, string measurement, Vector2 at, bool alignRight, Color color)
    {
        float w = MathF.Max(TextWidth(name), TextWidth(measurement));
        float x = alignRight ? at.X - w : at.X;
        float y = MathF.Round(at.Y);
        for (int tries = 0; tries < 16; tries++)
        {
            var rect = new Vectorier.Gameplay.Rl.Rectangle(x - 1, y, w + 2, LineHeight * 2 + 2);
            if (!placed.Any(r => Raylib.CheckCollisionRecs(r, rect)))
                break;
            y += LineHeight;
        }
        placed.Add(new Vectorier.Gameplay.Rl.Rectangle(x - 1, y, w + 2, LineHeight * 2 + 2));
        float nameX = alignRight ? x + w - TextWidth(name) : x;
        float measureX = alignRight ? x + w - TextWidth(measurement) : x;
        DrawOutlined(name, new Vector2(nameX, y), color, bold: true);
        DrawOutlined(measurement, new Vector2(measureX, y + LineHeight), Text, bold: false);
    }

    internal static void DrawOutlined(string text, Vector2 at, Color color, bool bold)
    {
        at = new Vector2(MathF.Round(at.X), MathF.Round(at.Y));
        if (at.X > Raylib.GetScreenWidth() || at.Y > Raylib.GetScreenHeight() || at.X + TextWidth(text) < 0 || at.Y + LineHeight < 0)
            return;
        foreach (var d in new[] { new Vector2(-1, 0), new Vector2(1, 0), new Vector2(0, -1), new Vector2(0, 1), new Vector2(1, 1) })
            GameFont.DrawUi(text, at.X + d.X, at.Y + d.Y, FontSize, Shadow);
        GameFont.DrawUi(text, at.X, at.Y, FontSize, color);
        if (bold)
            GameFont.DrawUi(text, at.X + 0.6f, at.Y, FontSize, color);
    }

    internal static void Dashed(Vector2 a, Vector2 b, Color color)
    {
        float length = Vector2.Distance(a, b);
        if (length < 0.5f)
            return;
        var dir = (b - a) / length;
        for (float d = 0; d < length; d += 9)
            Raylib.DrawLineEx(a + dir * d, a + dir * MathF.Min(d + 5, length), 1.5f, Fade(color, 0.8f));
    }

    internal static void DrawPanel(List<(string text, Color color, float indent)> lines)
    {
        const float x = 10, y = 26, pad = 6;
        float width = 0;
        foreach (var line in lines)
            width = MathF.Max(width, line.indent + TextWidth(line.text));
        var back = new Vectorier.Gameplay.Rl.Rectangle(x - pad, y - pad, width + pad * 2, lines.Count * LineHeight + pad * 2);
        Raylib.DrawRectangleRec(back, PanelBack);
        for (int i = 0; i < lines.Count; i++)
            GameFont.DrawUi(lines[i].text, x + lines[i].indent, y + i * LineHeight, FontSize, lines[i].color);
    }
}
