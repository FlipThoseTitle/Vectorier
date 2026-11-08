using Avalonia;
using Avalonia.Input;
using Vectorier.Services.Localization;

namespace Vectorier.Controls.Scene;

public partial class SceneView
{
    /// <summary>Blender-style transforms that follow the pointer until a click.</summary>
    public bool StartModal(char key)
    {
        if (_editor == null || Renderer == null || !_editor.HasSelection || _mode != InteractionMode.None)
            return false;
        var targets = _editor.GetTopmostSelection();
        _editor.BeginGesture();
        _targets = targets;
        _memento = new TransformMemento(targets);
        _pressScreen = _lastPointer;
        _pivotScreen = GizmoOrigin(Viewport, targets);
        _axis = '\0';
        _mode = key switch { 'G' => InteractionMode.ModalMove, 'R' => InteractionMode.ModalRotate, _ => InteractionMode.ModalScale };
        _editor.StatusText = key switch
        {
            'G' => Loc.T("Move: click to confirm, right click or Esc to cancel (X / Y lock an axis)"),
            'R' => Loc.T("Rotate: click to confirm, right click or Esc to cancel (Ctrl snaps 15°)"),
            _ => Loc.T("Scale: click to confirm, right click or Esc to cancel (X / Y lock an axis)"),
        };
        InvalidateVisual();
        return true;
    }

    private void StartStamp(Point pos)
    {
        if (_editor == null || !_editor.HasSelection)
            return;
        _editor.BeginGesture();
        var copies = _editor.DuplicateSelection();
        _editor.Select(copies);
        _targets = _editor.GetTopmostSelection();
        _memento = new TransformMemento(_targets);
        _pressScreen = pos;
        _mode = InteractionMode.Stamp;
        _editor.StatusText = Loc.T("Keep Alt held and click to place more copies; release Alt to stop.");
    }

    private void FinishGesture()
    {
        _mode = InteractionMode.None;
        _memento = null;
        _axis = '\0';
        _editor?.EndGesture();
        InvalidateVisual();
    }

    /// <summary>Esc / right click: reverts a modal transform or stamping.</summary>
    public bool CancelCurrent()
    {
        if (_editor == null)
            return false;
        if (_mode is InteractionMode.ModalMove or InteractionMode.ModalRotate or InteractionMode.ModalScale or InteractionMode.Stamp)
        {
            _mode = InteractionMode.None;
            _memento = null;
            _axis = '\0';
            _editor.CancelGesture();
            _editor.StatusText = Loc.T("Cancelled");
            InvalidateVisual();
            return true;
        }
        return false;
    }

    public bool IsModal => _mode is InteractionMode.ModalMove or InteractionMode.ModalRotate or InteractionMode.ModalScale or InteractionMode.Stamp;

    /// <summary>A transform of the selection is in progress (left drag, gizmo, Rect tool or G / R / S).</summary>
    public bool IsTransforming =>
        _memento != null && (_mode is InteractionMode.ModalMove or InteractionMode.ModalRotate or InteractionMode.ModalScale ||
                             _dragStarted && _mode is InteractionMode.Move or InteractionMode.Rotate or InteractionMode.Scale or InteractionMode.RectTransform);

    /// <summary>
    /// Ctrl+D while dragging or during G / R / S: the selection is duplicated where it is now, the copies become the
    /// selection and the transform goes on with them (the originals stay where they were left). The whole thing stays
    /// one undo step.
    /// </summary>
    public bool DuplicateDuringTransform()
    {
        if (_editor == null || Renderer == null || !IsTransforming)
            return false;
        var copies = _editor.DuplicateSelection();
        if (copies.Count == 0)
            return false;
        _editor.Select(copies);
        _targets = _editor.GetTopmostSelection();
        _memento = new TransformMemento(_targets);
        // The transform starts over from the copies' current state and the pointer's current place.
        _pressScreen = _lastPointer;
        if (_mode == InteractionMode.RectTransform)
            _rectStart = Renderer.ScreenBounds(Viewport, _targets);
        _editor.StatusText = Loc.F("Duplicated {0} element(s)", copies.Count);
        InvalidateVisual();
        return true;
    }

    /// <summary>Keys that only make sense in the Scene. Returns true when handled.</summary>
    public bool HandleKey(Key key, KeyModifiers mods)
    {
        if (_editor == null || IsGameView)
            return false;

        if (IsModal)
        {
            switch (key)
            {
                case Key.Escape:
                    return CancelCurrent();
                case Key.Enter:
                    if (_mode != InteractionMode.Stamp) FinishGesture();
                    return true;
                case Key.X:
                case Key.Y:
                    _axis = _axis == (char)('X' + (key - Key.X)) ? '\0' : key == Key.X ? 'X' : 'Y';
                    ApplyTransform(_lastPointer, mods);
                    return true;
            }
            return false;
        }

        if (mods != KeyModifiers.None && mods != KeyModifiers.Shift)
            return false;

        switch (key)
        {
            case Key.G when mods == KeyModifiers.None:
                return StartModal('G');
            case Key.R when mods == KeyModifiers.None && _editor.HasSelection:
                return StartModal('R');
            case Key.S when mods == KeyModifiers.None:
                return StartModal('S');
            case Key.F when mods == KeyModifiers.None:
                if (_editor.ActiveElement != null)
                    Frame(_editor.ActiveElement);
                else
                    FrameStart();
                return true;
            case Key.Left or Key.Right or Key.Up or Key.Down when _editor.HasSelection:
                {
                    double step = mods == KeyModifiers.Shift ? 10 : 1;
                    double dx = key == Key.Left ? -step : key == Key.Right ? step : 0;
                    double dy = key == Key.Up ? -step : key == Key.Down ? step : 0;
                    foreach (var t in _editor.GetTopmostSelection())
                    {
                        t.X += dx;
                        t.Y += dy;
                    }
                    return true;
                }
        }
        return false;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        // Releasing Alt ends stamping: the floating copy is removed.
        if (_mode == InteractionMode.Stamp && e.Key is Key.LeftAlt or Key.RightAlt)
            CancelCurrent();
    }
}
