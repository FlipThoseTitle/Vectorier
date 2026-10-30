using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Vectorier.Models.Scene;
using Vectorier.Services.Localization;

namespace Vectorier.Controls.Scene;

/// <summary>
/// Game View: the level as the game shows it, framed on the Camera's view (the selected Camera, or the first one in
/// the level). It is read-only: nothing can be selected, moved or placed from here.
/// </summary>
public sealed partial class SceneView
{
    public static readonly StyledProperty<bool> IsGameViewProperty =
        AvaloniaProperty.Register<SceneView, bool>(nameof(IsGameView));

    private static readonly IBrush LetterboxBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
    private static readonly IPen CameraFramePen = new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)), 1);

    public bool IsGameView
    {
        get => GetValue(IsGameViewProperty);
        set => SetValue(IsGameViewProperty, value);
    }

    private CameraElement? GameCamera()
    {
        if (_editor == null)
            return null;
        if (_editor.ActiveElement is CameraElement selected)
            return selected;
        return _editor.Document.Roots.SelectMany(SceneDocument.Walk).OfType<CameraElement>().FirstOrDefault();
    }

    private void RenderGame(DrawingContext context, Size size)
    {
        if (_renderer == null)
            return;
        var camera = GameCamera();
        var view = CameraElement.ViewSize;
        Point center;
        if (camera != null)
        {
            var parent = camera.ParentWorldPosition;
            center = new Point(parent.X + camera.X, parent.Y + camera.Y);
        }
        else
        {
            center = _center;
        }
        double zoom = Math.Max(1e-4, Math.Min(size.Width / view.Width, size.Height / view.Height));
        var viewport = new SceneViewport(center, zoom, size);
        var frame = new Rect(size.Width / 2 - view.Width * zoom / 2, size.Height / 2 - view.Height * zoom / 2, view.Width * zoom, view.Height * zoom);

        using (context.PushClip(frame))
        {
            context.FillRectangle(Background, frame);
            _renderer.Render(context, viewport, SceneRenderMode.Game, c => RenderMoveVisualizerModel(c, viewport));
        }

        // Outside the camera's view.
        context.FillRectangle(LetterboxBrush, new Rect(0, 0, size.Width, Math.Max(0, frame.Top)));
        context.FillRectangle(LetterboxBrush, new Rect(0, frame.Bottom, size.Width, Math.Max(0, size.Height - frame.Bottom)));
        context.FillRectangle(LetterboxBrush, new Rect(0, frame.Top, Math.Max(0, frame.Left), frame.Height));
        context.FillRectangle(LetterboxBrush, new Rect(frame.Right, frame.Top, Math.Max(0, size.Width - frame.Right), frame.Height));
        context.DrawRectangle(null, CameraFramePen, frame);

        string label = camera == null ? Loc.T("No Camera in this level: showing the Scene's center") : Loc.T("Game View (read-only)");
        var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, Brushes.White);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)), null, new Rect(6, 6, text.Width + 12, text.Height + 6), 3, 3);
        context.DrawText(text, new Point(12, 9));
    }
}
