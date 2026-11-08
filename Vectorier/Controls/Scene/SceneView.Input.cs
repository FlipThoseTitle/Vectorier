using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using Vectorier.Models.Scene;
using Vectorier.ViewModels.Editor;

namespace Vectorier.Controls.Scene;

internal enum InteractionMode
{
    None,
    Pan,
    Zoom,
    RubberBand,
    Move,
    Rotate,
    Scale,
    RectTransform,
    TrapezoidBase,
    PlaceDrag,
    ModalMove,
    ModalRotate,
    ModalScale,
    Stamp,
    DynamicSupport,
    AnimationDirection,
    SnapMove,
    SnapScale,
    Paint,
}

[Flags]
internal enum RectHandle
{
    None = 0,
    Left = 1,
    Right = 2,
    Top = 4,
    Bottom = 8,
    Move = 16,
    Rotate = 32,
}

public sealed partial class SceneView
{
    private const double DragThreshold = 4;

    private InteractionMode _mode;
    private Point _pressScreen;
    private Point _lastPointer;
    private Point _pivotScreen;
    private Point _startCenter;
    private double _startZoom;
    private bool _dragStarted;
    private bool _rubberAdd;
    private SceneElement? _pendingDrill;
    private List<SceneElement> _targets = new();
    private TransformMemento? _memento;
    private RectHandle _rectHandle;
    private Rect _rectStart;
    private char _axis; // 'X', 'Y' or '\0' for modal transforms
    private bool _altStampArmed;

    private EditorTool Tool => _editor?.ActiveTool ?? EditorTool.Move;

    // ================= POINTER ================= //

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        IsPointerInside = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        IsPointerInside = false;
        if (_editor != null)
            _editor.ScenePointer = null;
        if (!_ghostFromDrag)
            ClearGhost();
        if (_hoverHandle != GizmoHandle.None)
        {
            _hoverHandle = GizmoHandle.None;
            InvalidateVisual();
        }
    }

    /// <summary>The pointer is over the Scene (Blender shortcuts only apply there).</summary>
    public bool IsPointerInside { get; private set; }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (IsGameView)
            return;
        // Mouse wheel zooms; a touchpad pans with two fingers and zooms with a pinch (Ctrl + wheel), see TouchpadInput.
        if (!TouchpadInput.IsPinch(e) && TouchpadInput.IsTouchpadScroll(e))
        {
            StopFrameAnimation();
            double speed = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? 3 : 1;
            _center -= e.Delta * TouchpadInput.PixelsPerUnit / Zoom * speed;
            UpdateEditorCenter();
            InvalidateVisual();
        }
        else
        {
            ZoomAt(e.GetPosition(this), TouchpadInput.WheelZoomFactor(e));
        }
        e.Handled = true;
    }

    /// <summary>macOS touchpad pinch.</summary>
    private void OnTouchPadMagnify(object? sender, PointerDeltaEventArgs e)
    {
        if (IsGameView)
            return;
        ZoomAt(e.GetPosition(this), TouchpadInput.MagnifyFactor(e));
        e.Handled = true;
    }

    protected override async void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (IsGameView)
            return;
        if (_editor == null || Renderer == null)
            return;
        Focus();
        var point = e.GetCurrentPoint(this);
        var pos = point.Position;
        var mods = e.KeyModifiers;
        _lastPointer = pos;

        // Modal transforms: left confirms, right cancels.
        // Dynamic Editor: drag the custom-ease support handle.
        if (point.Properties.IsLeftButtonPressed && _editor.Dynamics.ActiveTrack is { } dynTrack &&
            DynamicSupportHandle(Viewport, dynTrack) is { } support &&
            Math.Abs(pos.X - support.Handle.X) <= 6 && Math.Abs(pos.Y - support.Handle.Y) <= 6)
        {
            _mode = InteractionMode.DynamicSupport;
            _editor.BeginGesture(); // the whole drag is one undo step
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        // A Moving Animation: drag the tip of its direction arrow.
        if (point.Properties.IsLeftButtonPressed && _mode == InteractionMode.None && TryStartAnimationDirection(e, pos))
            return;

        if (_mode is InteractionMode.ModalMove or InteractionMode.ModalRotate or InteractionMode.ModalScale or InteractionMode.Stamp)
        {
            if (point.Properties.IsLeftButtonPressed)
            {
                bool stamp = _mode == InteractionMode.Stamp;
                FinishGesture();
                if (stamp && (mods & KeyModifiers.Alt) != 0)
                    StartStamp(pos);
            }
            else if (point.Properties.IsRightButtonPressed)
            {
                CancelCurrent();
            }
            e.Handled = true;
            return;
        }

        // Space held: the left button pans (like the right button).
        if (SpacePanHeld && point.Properties.IsLeftButtonPressed)
        {
            StopFrameAnimation();
            _pressScreen = pos;
            _startCenter = _center;
            _startZoom = Zoom;
            _mode = InteractionMode.Pan;
            _rightMoved = true; // never opens the Create menu
            Cursor = HandCursor;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        // Middle button: held and moved it pans; clicked and released without moving it selects the element's
        // highest root Object (however deeply it is nested), see OnPointerReleased.
        if (point.Properties.IsMiddleButtonPressed)
        {
            StopFrameAnimation();
            _pressScreen = pos;
            _startCenter = _center;
            _startZoom = Zoom;
            _mode = InteractionMode.Pan;
            _rightMoved = false;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (point.Properties.IsRightButtonPressed)
        {
            StopFrameAnimation();
            _pressScreen = pos;
            _startCenter = _center;
            _startZoom = Zoom;
            // Right drag pans (hand cursor); Alt + right drag zooms (magnifier), like Unity.
            _mode = (mods & KeyModifiers.Alt) != 0 ? InteractionMode.Zoom : InteractionMode.Pan;
            _zoomUseY = false;
            _rightMoved = false;
            Cursor = _mode == InteractionMode.Zoom ? ZoomCursor : HandCursor;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (point.Properties.IsMiddleButtonPressed || !point.Properties.IsLeftButtonPressed)
            return;

        // Move Visualizer placement: a left click places the runner (selection is left alone).
        if (_editor.MoveVisualizer.IsPlacementActive)
        {
            e.Handled = true;
            await _editor.MoveVisualizer.PlaceFromSceneAsync(ScreenToLevel(pos));
            return;
        }

        // Brush: paint (or start a new image) instead of selecting.
        if (Tool == EditorTool.Brush && _editor.PlacementType == null)
        {
            await TryPaintPressAsync(e, pos);
            return;
        }

        _pressScreen = pos;
        _dragStarted = false;
        _pendingDrill = null;
        e.Pointer.Capture(this);
        e.Handled = true;

        // Placement armed from the Element Bar / Project Window.
        if (_editor.PlacementType is { } placing)
        {
            if (placing is ElementType.Platform or ElementType.Trigger or ElementType.Area or ElementType.Comment)
            {
                _mode = InteractionMode.PlaceDrag;
                return;
            }
            await PlaceAsync(placing, pos, null);
            return;
        }

        // Transform snapping (V: everything, C: platforms; A scales instead).
        if (TryStartSnapDrag(pos))
            return;

        // Gizmo handles win over whatever is below them.
        // A double click picks the next element underneath, even over the gizmo of the current one.
        if (e.ClickCount < 2 && (mods & (KeyModifiers.Control | KeyModifiers.Meta | KeyModifiers.Alt)) == 0 && TryStartGizmoDrag(pos))
            return;
        _gizmoHandle = GizmoHandle.None;

        // Trapezoid base-height handle.
        if (TrapezoidHandle(Viewport) is { } handle && handle.Inflate(3).Contains(pos))
        {
            BeginTransform(InteractionMode.TrapezoidBase, _editor.GetTopmostSelection());
            return;
        }

        // Rect tool zones on the current selection.
        if (Tool == EditorTool.Rect && _editor.HasSelection && e.ClickCount < 2)
        {
            var zone = RectZone(pos);
            if (zone != RectHandle.None && (mods & KeyModifiers.Alt) == 0)
            {
                _rectHandle = zone;
                _rectStart = Renderer.ScreenBounds(Viewport, _editor.GetTopmostSelection());
                _pivotScreen = _rectStart.Center;
                BeginTransform(InteractionMode.RectTransform, _editor.GetTopmostSelection());
                return;
            }
        }

        var hit = Renderer.HitTest(Viewport, pos);
        bool ctrl = (mods & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        bool shift = (mods & KeyModifiers.Shift) != 0;

        if (hit == null)
        {
            // Rotate/Scale drag on empty space still transform the selection; otherwise rubber band.
            if (_editor.HasSelection && !ctrl && !shift && Tool is EditorTool.Rotate or EditorTool.Scale)
            {
                StartPivotTransform(pos);
                return;
            }
            _rubberAdd = ctrl || shift;
            _clickTarget = null;
            _mode = InteractionMode.RubberBand;
            return;
        }

        var target = ResolveClick(hit.Owner, ctrl || shift ? 1 : e.ClickCount, pos);
        if (ctrl || shift)
        {
            _editor.Select(target, SelectMode.Toggle);
            _mode = InteractionMode.None;
            return;
        }

        if ((mods & KeyModifiers.Alt) != 0)
        {
            // Alt + drag duplicates the selection and drags the copies.
            if (!target.IsSelected)
                _editor.Select(target);
            _editor.BeginGesture();
            var copies = _editor.DuplicateSelection();
            _editor.Select(copies);
            _targets = _editor.GetTopmostSelection();
            _memento = new TransformMemento(_targets);
            _mode = InteractionMode.Move;
            _dragStarted = true;
            _altStampArmed = true;
            return;
        }

        // An element that is not selected yet is selected when the button is released: dragging from it draws a
        // selection rectangle instead of moving it. Selected elements are dragged as usual.
        if (!target.IsSelected)
        {
            _clickTarget = target;
            _rubberAdd = false;
            _mode = InteractionMode.RubberBand;
            return;
        }

        switch (Tool)
        {
            case EditorTool.Rotate:
            case EditorTool.Scale:
                StartPivotTransform(pos);
                break;
            default:
                _targets = _editor.GetTopmostSelection();
                _mode = InteractionMode.Move;
                break;
        }
    }

    /// <summary>Element pressed but not selected yet: a release without dragging selects it.</summary>
    private SceneElement? _clickTarget;

    /// <summary>Space held over the Scene: left dragging pans the view.</summary>
    public bool SpacePanHeld
    {
        get; set
        {
            if (field == value)
                return;
            field = value;
            if (_mode == InteractionMode.None)
                Cursor = value ? HandCursor : null;
        }
    }

    /// <summary>
    /// Picking: a click selects the topmost (deepest) element under the pointer. Each further double click selects
    /// the next element underneath; when there is nothing more underneath, the root Object of the current element
    /// is selected, then it loops back to the topmost element. Only elements of the same root Object are cycled.
    /// </summary>
    private SceneElement? _cyclePick;

    private SceneElement ResolveClick(SceneElement element, int clickCount, Point screen)
    {
        if (Renderer == null || _editor == null)
            return element;
        var hits = Renderer.HitTestAll(Viewport, screen);
        var current = _editor.ActiveElement;
        if (clickCount < 2)
        {
            // An element reached by double clicking stays selected when clicked again (so it can be dragged and the
            // next double click continues from it). Anything else: the click picks by priority.
            if (current is { IsSelected: true } && ReferenceEquals(current, _cyclePick)
                && hits.Any(h => ReferenceEquals(h, current) || h.IsDescendantOf(current)))
                return current;
            _cyclePick = null;
            return element;
        }
        // Cycle from the top: loose elements in front, then the first Object reached (its elements, then the Object
        // itself). An Object hides whatever is behind it, so elements outside it further down are never reached.
        var anchor = current is { IsSelected: true } && (hits.Contains(current) || hits.Any(h => h.IsDescendantOf(current)))
            ? current : element;
        var cycle = new List<SceneElement>();
        foreach (var h in hits)
        {
            var root = h.Root;
            if (ReferenceEquals(root, h) && h is not ObjectElement)
            {
                cycle.Add(h);
                continue;
            }
            cycle.AddRange(hits.Where(x => ReferenceEquals(x.Root, root)));
            cycle.Add(root);
            break;
        }
        cycle = cycle.Distinct().ToList();
        if (cycle.Count == 0)
            return element;
        int index = cycle.IndexOf(anchor);
        if (index < 0)
            return _cyclePick = cycle[0];
        // Past the last element underneath, loop back to the topmost one (like Unity).
        return _cyclePick = cycle[(index + 1) % cycle.Count];
    }

    private void StartPivotTransform(Point pos)
    {
        if (_editor == null || Renderer == null)
            return;
        var targets = _editor.GetTopmostSelection();
        _pivotScreen = GizmoOrigin(Viewport, targets);
        _gizmoHandle = Tool == EditorTool.Rotate ? GizmoHandle.Rotate : GizmoHandle.None;
        BeginTransform(Tool == EditorTool.Rotate ? InteractionMode.Rotate : InteractionMode.Scale, targets);
    }

    private void BeginTransform(InteractionMode mode, List<SceneElement> targets)
    {
        _targets = targets;
        _mode = mode;
        _dragStarted = false;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (IsGameView)
            return;
        var pos = e.GetPosition(this);
        var previous = _lastPointer;
        _lastPointer = pos;
        if (_editor == null || Renderer == null)
            return;
        _editor.ScenePointer = ScreenToLevel(pos);

        if (_mode == InteractionMode.Paint || Tool == EditorTool.Brush)
        {
            PaintMoved(pos);
            if (_mode == InteractionMode.Paint)
                return;
        }

        switch (_mode)
        {
            case InteractionMode.None:
                if (_editor.MoveVisualizer.IsPlacementActive)
                    _editor.MoveVisualizer.UpdatePlacementPreview(ScreenToLevel(pos));
                UpdatePlacementGhost(pos);
                if (UpdateSnapHover(pos))
                    return;
                var hover = HitGizmo(pos);
                if (hover != _hoverHandle)
                {
                    _hoverHandle = hover;
                    InvalidateVisual();
                }
                UpdateCursor(pos);
                return;
            case InteractionMode.Pan:
                {
                    if (pos != _pressScreen)
                    {
                        _rightMoved = true;
                        Cursor = HandCursor; // a middle click only shows the hand once it moves
                    }
                    double speed = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? 3 : 1;
                    _center -= (pos - previous) / Zoom * speed;
                    UpdateEditorCenter();
                    InvalidateVisual();
                    return;
                }
            case InteractionMode.Zoom:
                {
                    double speed = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? 3 : 1;
                    ZoomAt(_pressScreen, Math.Exp(NiceMouseDelta(pos - previous) * 0.005 * speed));
                    return;
                }
            case InteractionMode.RubberBand:
            case InteractionMode.PlaceDrag:
                InvalidateVisual();
                return;
            case InteractionMode.AnimationDirection:
                DragAnimationDirection(pos);
                return;
            case InteractionMode.DynamicSupport:
                {
                    if (_editor.Dynamics.ActiveTrack is { } track && _editor.Dynamics.CurrentSegment(track) is { } seg)
                    {
                        var element = track.Element;
                        var level = Viewport.ScreenToLevel(pos, Renderer.GroupFor(element));
                        var origin = element.ParentWorldPosition;
                        var off = PivotToCenter(element);
                        _editor.Dynamics.SetSupport(track, seg.A, seg.B, new Point(level.X - origin.X - off.X, level.Y - origin.Y - off.Y));
                    }
                    InvalidateVisual();
                    return;
                }
            case InteractionMode.ModalMove:
            case InteractionMode.ModalRotate:
            case InteractionMode.ModalScale:
            case InteractionMode.Stamp:
                ApplyTransform(pos, e.KeyModifiers);
                return;
            case InteractionMode.SnapMove:
            case InteractionMode.SnapScale:
                ApplySnap(pos);
                return;
        }

        if (!_dragStarted)
        {
            if (Math.Abs(pos.X - _pressScreen.X) < DragThreshold && Math.Abs(pos.Y - _pressScreen.Y) < DragThreshold)
                return;
            _dragStarted = true;
            _pendingDrill = null;
            _editor.BeginGesture();
            _memento = new TransformMemento(_targets);
        }
        else if (_memento == null)
        {
            _memento = new TransformMemento(_targets);
        }
        ApplyTransform(pos, e.KeyModifiers);
    }

    protected override async void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (IsGameView)
            return;
        var pos = e.GetPosition(this);
        if (_editor == null || Renderer == null)
            return;

        if (_mode == InteractionMode.Paint)
        {
            EndPaintStroke();
            e.Pointer.Capture(null);
            return;
        }

        switch (_mode)
        {
            case InteractionMode.AnimationDirection:
                EndAnimationDirection();
                _mode = InteractionMode.None;
                e.Pointer.Capture(null);
                UpdateCursor(pos);
                return;
            case InteractionMode.Pan:
            case InteractionMode.Zoom:
            case InteractionMode.DynamicSupport:
                {
                    // A right click without any movement at all opens the Create menu.
                    bool menu = _mode == InteractionMode.Pan && !_rightMoved && pos == _pressScreen && e.InitialPressMouseButton == MouseButton.Right;
                    // A middle click without any movement selects the root Object under the pointer.
                    bool pickRoot = _mode == InteractionMode.Pan && !_rightMoved && e.InitialPressMouseButton == MouseButton.Middle;
                    if (_mode == InteractionMode.DynamicSupport)
                        _editor.EndGesture();
                    _mode = InteractionMode.None;
                    e.Pointer.Capture(null);
                    Cursor = null;
                    UpdateCursor(pos);
                    if (menu)
                    {
                        // The element under the pointer is picked as a left click would pick it (a selection that
                        // already holds it is kept, so the menu acts on all of it).
                        SceneElement? picked = Renderer.HitTest(Viewport, pos) is { } menuHit ? ResolveClick(menuHit.Owner, 1, pos) : null;
                        if (picked != null && !picked.IsSelected)
                            _editor.Select(picked);
                        ContextMenuRequested?.Invoke(picked, ScreenToLevel(pos));
                    }
                    if (pickRoot && Renderer.HitTest(Viewport, pos) is { } rootHit)
                    {
                        var root = rootHit.Owner.Root;
                        _editor.Select(root);
                        _editor.RevealInHierarchy(root);
                    }
                    return;
                }

            case InteractionMode.SnapMove:
            case InteractionMode.SnapScale:
                e.Pointer.Capture(null);
                FinishSnapDrag();
                return;

            case InteractionMode.RubberBand:
                {
                    _mode = InteractionMode.None;
                    e.Pointer.Capture(null);
                    var rect = RectEx.Box(_pressScreen, pos);
                    var clicked = _clickTarget;
                    _clickTarget = null;
                    if (rect.Width < DragThreshold && rect.Height < DragThreshold)
                    {
                        if (clicked != null)
                            _editor.Select(clicked);
                        else if (!_rubberAdd)
                            _editor.ClearSelection();
                    }
                    else
                    {
                        var found = Renderer.QueryRect(Viewport, rect);
                        _editor.Select(found, _rubberAdd ? SelectMode.Add : SelectMode.Replace);
                    }
                    InvalidateVisual();
                    return;
                }

            case InteractionMode.PlaceDrag:
                {
                    _mode = InteractionMode.None;
                    e.Pointer.Capture(null);
                    var placing = _editor.PlacementType;
                    if (placing == null)
                        return;
                    var a = ScreenToLevel(_pressScreen);
                    var b = ScreenToLevel(pos);
                    var rect = RectEx.Box(a, b);
                    bool sized = Math.Abs(pos.X - _pressScreen.X) >= DragThreshold && Math.Abs(pos.Y - _pressScreen.Y) >= DragThreshold;
                    if (sized)
                    {
                        if (placing == ElementType.Platform)
                            rect = new Rect(Math.Round(rect.X), Math.Round(rect.Y), Math.Max(1, Math.Round(rect.Width)), Math.Max(1, Math.Round(rect.Height)));
                        await PlaceAsync(placing.Value, _pressScreen, rect);
                    }
                    else
                    {
                        await PlaceAsync(placing.Value, pos, null);
                    }
                    InvalidateVisual();
                    return;
                }

            case InteractionMode.Move:
            case InteractionMode.Rotate:
            case InteractionMode.Scale:
            case InteractionMode.RectTransform:
            case InteractionMode.TrapezoidBase:
                {
                    // Read before releasing the capture: that raises OnPointerCaptureLost, which already resets _mode.
                    var released = _mode;
                    bool dragged = _dragStarted;
                    e.Pointer.Capture(null);
                    if (dragged)
                    {
                        switch (released)
                        {
                            case InteractionMode.Move: Services.Tutorial.TutorialSignals.MoveDrags++; break;
                            case InteractionMode.Rotate: Services.Tutorial.TutorialSignals.RotateDrags++; break;
                            case InteractionMode.Scale: Services.Tutorial.TutorialSignals.ScaleDrags++; break;
                            case InteractionMode.RectTransform: Services.Tutorial.TutorialSignals.RectDrags++; break;
                        }
                    }
                    _mode = InteractionMode.None;
                    _gizmoHandle = GizmoHandle.None;
                    _memento = null;
                    if (dragged)
                        _editor.EndGesture();
                    else if (_pendingDrill != null)
                        _editor.Select(_pendingDrill);
                    _pendingDrill = null;

                    // Keep Alt held after an Alt-drag to keep stamping duplicates.
                    if (_altStampArmed && (e.KeyModifiers & KeyModifiers.Alt) != 0)
                    {
                        _altStampArmed = false;
                        StartStamp(pos);
                    }
                    _altStampArmed = false;
                    InvalidateVisual();
                    return;
                }
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndPaintStroke();
        if (_mode is InteractionMode.Pan or InteractionMode.Zoom or InteractionMode.RubberBand or InteractionMode.PlaceDrag)
        {
            _mode = InteractionMode.None;
            Cursor = null;
            InvalidateVisual();
        }
        else if (_mode is InteractionMode.SnapMove or InteractionMode.SnapScale)
        {
            FinishSnapDrag();
        }
        else if (_mode is InteractionMode.Move or InteractionMode.Rotate or InteractionMode.Scale or InteractionMode.RectTransform or InteractionMode.TrapezoidBase)
        {
            if (_dragStarted)
                _editor?.EndGesture();
            _mode = InteractionMode.None;
            _memento = null;
        }
        _gizmoHandle = GizmoHandle.None;
    }

    /// <summary>
    /// Unity's zoom drag: the dominant axis of the movement gives the sign (right / up zoom in), with a little
    /// hysteresis so diagonal drags do not flip between zooming in and out.
    /// </summary>
    private double NiceMouseDelta(Vector d)
    {
        double dx = d.X, dy = -d.Y;
        double ax = Math.Abs(dx), ay = Math.Abs(dy), max = Math.Max(ax, ay);
        if (max <= 0)
            return 0;
        if (Math.Abs(ax - ay) / max > 0.1)
            _zoomUseY = ay > ax;
        double magnitude = Math.Sqrt(dx * dx + dy * dy);
        return (_zoomUseY ? Math.Sign(dy) : Math.Sign(dx)) * magnitude;
    }

    private bool _zoomUseY;
    private bool _rightMoved;

    /// <summary>Right click (no movement) in the Scene: element under the pointer (or null) and the level point.</summary>
    public event Action<SceneElement?, Point>? ContextMenuRequested;

    private void OnSceneDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (IsGameView || _editor == null || Renderer == null || _editor.PlacementType != null)
            return;
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta | KeyModifiers.Shift)) != 0)
            return;
        // Selection itself happens on press (ResolveClick); the double click only reveals it in the Hierarchy.
        if (_editor.ActiveElement is { } active)
            _editor.RevealInHierarchy(active);
    }

    private async System.Threading.Tasks.Task PlaceAsync(ElementType type, Point screen, Rect? levelRect)
    {
        if (_editor == null)
            return;
        var source = _editor.ActiveElementType?.Type == type ? _editor.ProjectWindow.CurrentSource : null;
        Point position = levelRect?.TopLeft ?? ScreenToLevel(screen);
        Size? size = levelRect is { } r ? new Size(Math.Max(1, r.Width), Math.Max(1, r.Height)) : null;
        var created = await _editor.CreateElementAsync(type, position, size, source);
        if (created != null)
            _editor.Select(created);
    }
}
