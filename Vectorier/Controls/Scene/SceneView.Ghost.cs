using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Vectorier.Models.Scene;
using Vectorier.ViewModels.Editor;

namespace Vectorier.Controls.Scene;

/// <summary>
/// Semi-transparent preview of what a click (or a drop from the Project Window) will create, following the pointer.
/// </summary>
public sealed partial class SceneView
{
    private const double GhostOpacity = 0.5;

    private SceneElement? _ghost;
    private object? _ghostKey;
    private Vector _ghostOffset;
    private bool _ghostLoading;
    private bool _ghostFromDrag;

    /// <summary>Builds (once per asset) and moves the preview to the pointer.</summary>
    private void UpdateGhost(Point screen, ElementType? type, ElementSource? source, object? identity)
    {
        if (_editor == null || type == null)
        {
            ClearGhost();
            return;
        }
        var level = ScreenToLevel(screen);
        var key = (type.Value, identity ?? (object?)source?.TextureName ?? source?.ModelName ?? (object?)source?.Definition ?? source?.Trigger ?? (object?)source?.Area ?? source?.ItemType);
        if (_ghost == null || !Equals(_ghostKey, key))
        {
            _ghost = _editor.BuildPlacementGhost(type.Value, level, source);
            _ghostKey = _ghost == null ? null : key;
            if (_ghost == null)
            {
                RequestGhostAsset(source);
                InvalidateVisual();
                return;
            }
            _ghostOffset = new Vector(_ghost.X - level.X, _ghost.Y - level.Y);
        }
        double x = Math.Round(level.X + _ghostOffset.X), y = Math.Round(level.Y + _ghostOffset.Y);
        if (_ghost.X != x || _ghost.Y != y)
        {
            _ghost.X = x;
            _ghost.Y = y;
        }
        InvalidateVisual();
    }

    /// <summary>Textures are loaded the first time they are previewed; the ghost appears once they are ready.</summary>
    private async void RequestGhostAsset(ElementSource? source)
    {
        if (_editor == null || _ghostLoading || source?.TextureName is not { } name)
            return;
        _ghostLoading = true;
        try
        {
            await _editor.Context.Textures.GetAsync(name);
        }
        catch
        {
            // Missing texture: no preview.
        }
        finally
        {
            _ghostLoading = false;
        }
        if (_pointerInside && _mode == InteractionMode.None && _editor.PlacementType is { } t)
            UpdateGhost(_lastPointer, t, _editor.ProjectWindow.CurrentSource, _editor.ProjectWindow.SelectedItem);
    }

    private void ClearGhost()
    {
        if (_ghost == null && _ghostKey == null)
            return;
        _ghost = null;
        _ghostKey = null;
        _ghostFromDrag = false;
        InvalidateVisual();
    }

    /// <summary>Placement armed and the pointer over the Scene: preview follows it.</summary>
    private void UpdatePlacementGhost(Point screen)
    {
        if (_editor?.PlacementType is { } type && !_editor.MoveVisualizer.IsPlacementActive)
            UpdateGhost(screen, type, _editor.ActiveElementType?.Type == type ? _editor.ProjectWindow.CurrentSource : null, _editor.ProjectWindow.SelectedItem);
        else
            ClearGhost();
    }

    private void RenderPlacementGhost(DrawingContext context, SceneViewport viewport)
    {
        if (_ghost == null || _renderer == null)
            return;
        if (_mode == InteractionMode.PlaceDrag)
            return; // the dragged rectangle replaces it
        _renderer.RenderGhost(context, viewport, _ghost, GhostOpacity, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
    }
}
