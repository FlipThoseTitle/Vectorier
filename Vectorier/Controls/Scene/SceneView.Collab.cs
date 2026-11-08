using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Vectorier.Models.Scene;
using Vectorier.Services.Collab;

namespace Vectorier.Controls.Scene;

/// <summary>Live Collaboration: the others' pointers (a circle with their name) and what they have selected, in their colors.</summary>
public sealed partial class SceneView
{
    private static readonly Typeface PresenceTypeface = new("Inter, Segoe UI, Arial", FontStyle.Normal, FontWeight.SemiBold);
    private readonly Dictionary<uint, (IBrush Brush, Pen Pen)> _presencePaints = new();

    private (IBrush Brush, Pen Pen) PresencePaint(uint color)
    {
        if (!_presencePaints.TryGetValue(color, out var paint))
        {
            var brush = new SolidColorBrush(Color.FromUInt32(color | 0xFF000000));
            paint = (brush, new Pen(brush, 2, new DashStyle(new double[] { 4, 2 }, 0)));
            _presencePaints[color] = paint;
        }
        return paint;
    }

    private static IEnumerable<(CollabUser User, PresenceMsg Presence)> Others()
    {
        var session = CollabSession.Current;
        if (!session.IsActive)
            yield break;
        foreach (var (id, presence) in session.Presence)
        {
            if (id != session.MyId && session.UserById(id) is { } user)
                yield return (user, presence);
        }
    }

    private void RenderCollabSelections(DrawingContext context, SceneViewport viewport)
    {
        if (Renderer == null || _editor == null || CollabSession.Current.LevelSync is not { } sync)
            return;
        foreach (var (user, presence) in Others())
        {
            if (presence.Selection.Count == 0)
                continue;
            var elements = new List<SceneElement>();
            foreach (long id in presence.Selection)
            {
                if (sync.FindElement(id) is { } element)
                    elements.Add(element);
            }
            if (elements.Count > 0)
                Renderer.RenderSelection(context, viewport, elements, PresencePaint(user.Color).Pen);
        }
    }

    private void RenderCollabPresence(DrawingContext context)
    {
        var session = CollabSession.Current;
        foreach (var (user, presence) in Others())
        {
            if (double.IsNaN(presence.X) || double.IsNaN(presence.Y))
                continue;
            var at = LevelToScreen(new Point(presence.X, presence.Y));
            if (!new Rect(Bounds.Size).Inflate(40).Contains(at))
                continue;
            var (brush, _) = PresencePaint(user.Color);
            context.DrawEllipse(brush, new Pen(Brushes.White, 1.5), at, 6, 6);
            var text = new FormattedText(session.DisplayName(user), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, PresenceTypeface, 11, Brushes.White);
            var box = new Rect(at.X + 9, at.Y + 6, text.Width + 10, text.Height + 4);
            context.DrawRectangle(brush, null, box, 3, 3);
            context.DrawText(text, new Point(box.X + 5, box.Y + 2));
        }
    }
}
