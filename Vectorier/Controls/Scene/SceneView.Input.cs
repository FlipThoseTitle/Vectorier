using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using Vectorier.Models.Scene;
using Vectorier.ViewModels.Editor;
using Vectorier.Services.Localization;

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

/// <summary>Transform values of a set of elements and their descendants, restored before each step of a drag.</summary>
internal sealed class TransformMemento
{
    private readonly record struct State(SceneElement Element, double X, double Y, double W, double H,
        double Rotation, double Skew, bool FlipX, bool FlipY, double BaseHeight, bool AutoSize);

    private readonly List<State> _states = new();

    public TransformMemento(IEnumerable<SceneElement> roots)
    {
        foreach (var root in roots)
        {
            foreach (var e in SceneDocument.Walk(root))
            {
                var image = e as ImageElement;
                _states.Add(new State(e, e.X, e.Y, e.Width, e.Height,
                    image?.Rotation ?? 0, image?.Skew ?? 0, image?.FlipX ?? false, image?.FlipY ?? false,
                    (e as TrapezoidElement)?.BaseHeight ?? 0, image?.AutoSize ?? false));
            }
        }
    }

    public void Restore()
    {
        foreach (var s in _states)
        {
            var e = s.Element;
            e.X = s.X;
            e.Y = s.Y;
            e.Width = s.W;
            e.Height = s.H;
            if (e is ImageElement image)
            {
                image.Rotation = s.Rotation;
                image.Skew = s.Skew;
                image.FlipX = s.FlipX;
                image.FlipY = s.FlipY;
                image.AutoSize = s.AutoSize;
            }
            else if (e is TrapezoidElement trapezoid)
            {
                trapezoid.BaseHeight = s.BaseHeight;
            }
        }
    }

    public (double X, double Y) StartPosition(SceneElement element)
    {
        foreach (var s in _states)
        {
            if (ReferenceEquals(s.Element, element))
                return (s.X, s.Y);
        }
        return (element.X, element.Y);
    }
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
    private bool _pointerInside;
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
        _pointerInside = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _pointerInside = false;
        if (!_ghostFromDrag)
            ClearGhost();
        if (_hoverHandle != GizmoHandle.None)
        {
            _hoverHandle = GizmoHandle.None;
            InvalidateVisual();
        }
    }

    /// <summary>The pointer is over the Scene (Blender shortcuts only apply there).</summary>
    public bool IsPointerInside => _pointerInside;

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (IsGameView)
            return;
        ZoomAt(e.GetPosition(this), Math.Pow(1.15, e.Delta.Y));
        e.Handled = true;
    }

    protected override async void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (IsGameView)
            return;
        if (_editor == null || _renderer == null)
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
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

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
            _startZoom = _zoom;
            _mode = InteractionMode.Pan;
            _rightMoved = true; // never opens the Create menu
            Cursor = HandCursor;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        // Middle click: select the element's highest root Object (however deeply it is nested).
        if (point.Properties.IsMiddleButtonPressed)
        {
            if (_renderer.HitTest(Viewport, pos) is { } rootHit)
            {
                var root = rootHit.Owner.Root;
                _editor.Select(root);
                _editor.RevealInHierarchy(root);
            }
            e.Handled = true;
            return;
        }

        if (point.Properties.IsRightButtonPressed)
        {
            StopFrameAnimation();
            _pressScreen = pos;
            _startCenter = _center;
            _startZoom = _zoom;
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
                _rectStart = _renderer.ScreenBounds(Viewport, _editor.GetTopmostSelection());
                _pivotScreen = _rectStart.Center;
                BeginTransform(InteractionMode.RectTransform, _editor.GetTopmostSelection());
                return;
            }
        }

        var hit = _renderer.HitTest(Viewport, pos);
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

    private bool _spacePanHeld;

    /// <summary>Space held over the Scene: left dragging pans the view.</summary>
    public bool SpacePanHeld
    {
        get => _spacePanHeld;
        set
        {
            if (_spacePanHeld == value)
                return;
            _spacePanHeld = value;
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
        if (_renderer == null || _editor == null)
            return element;
        var hits = _renderer.HitTestAll(Viewport, screen);
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
        if (_editor == null || _renderer == null)
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
        if (_editor == null || _renderer == null)
            return;

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
                    _rightMoved = true;
                double speed = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? 3 : 1;
                _center -= (pos - previous) / _zoom * speed;
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
            case InteractionMode.DynamicSupport:
            {
                if (_editor.Dynamics.ActiveTrack is { } track && _editor.Dynamics.CurrentSegment(track) is { } seg)
                {
                    var element = track.Element;
                    var level = Viewport.ScreenToLevel(pos, _renderer.GroupFor(element));
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
        if (_editor == null || _renderer == null)
            return;

        if (_mode == InteractionMode.Paint)
        {
            EndPaintStroke();
            e.Pointer.Capture(null);
            return;
        }

        switch (_mode)
        {
            case InteractionMode.Pan:
            case InteractionMode.Zoom:
            case InteractionMode.DynamicSupport:
            {
                // A right click without any movement at all opens the Create menu.
                bool menu = _mode == InteractionMode.Pan && !_rightMoved && pos == _pressScreen && e.InitialPressMouseButton == MouseButton.Right;
                _mode = InteractionMode.None;
                e.Pointer.Capture(null);
                Cursor = null;
                UpdateCursor(pos);
                if (menu)
                    ContextMenuRequested?.Invoke(_renderer.HitTest(Viewport, pos)?.Owner.Root, ScreenToLevel(pos));
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
                    var found = _renderer.QueryRect(Viewport, rect);
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
                e.Pointer.Capture(null);
                bool dragged = _dragStarted;
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
        if (IsGameView || _editor == null || _renderer == null || _editor.PlacementType != null)
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

    // ================= TRANSFORMS ================= //

    private Point LevelPointFor(SceneElement element, Point screen)
    {
        var group = _renderer!.GroupFor(element);
        var level = Viewport.ScreenToLevel(screen, group);
        var parent = element.ParentWorldPosition;
        return new Point(level.X - parent.X, level.Y - parent.Y);
    }

    private double PixelsToLevel(SceneElement element) => 1 / (_zoom * _renderer!.GroupFor(element).FrameScale);

    private void ApplyTransform(Point pos, KeyModifiers mods)
    {
        if (_editor == null || _renderer == null || _memento == null)
            return;
        _memento.Restore();
        bool ctrl = (mods & KeyModifiers.Control) != 0;
        bool shift = (mods & KeyModifiers.Shift) != 0;

        switch (_mode)
        {
            case InteractionMode.Move:
            case InteractionMode.ModalMove:
            case InteractionMode.Stamp:
            {
                Vector delta = new(pos.X - _pressScreen.X, pos.Y - _pressScreen.Y);
                if (_mode == InteractionMode.ModalMove && _axis == 'X') delta = new Vector(delta.X, 0);
                else if (_mode == InteractionMode.ModalMove && _axis == 'Y') delta = new Vector(0, delta.Y);
                else if (_mode == InteractionMode.Move && _gizmoHandle is GizmoHandle.MoveX or GizmoHandle.MoveY) delta = ConstrainMove(delta);
                else if (shift && _mode == InteractionMode.Move) delta = Math.Abs(delta.X) >= Math.Abs(delta.Y) ? new Vector(delta.X, 0) : new Vector(0, delta.Y);
                foreach (var t in _targets)
                {
                    double k = PixelsToLevel(t);
                    var (sx, sy) = _memento.StartPosition(t);
                    t.X = sx + delta.X * k;
                    t.Y = sy + delta.Y * k;
                    if (t.IntegerTransform)
                    {
                        t.X = Math.Round(t.X);
                        t.Y = Math.Round(t.Y);
                    }
                }
                break;
            }

            case InteractionMode.Rotate:
            case InteractionMode.ModalRotate:
            {
                double a0 = Math.Atan2(_pressScreen.Y - _pivotScreen.Y, _pressScreen.X - _pivotScreen.X);
                double a1 = Math.Atan2(pos.Y - _pivotScreen.Y, pos.X - _pivotScreen.X);
                double angle = a1 - a0;
                if (ctrl)
                {
                    double step = Math.PI / 12;
                    angle = Math.Round(angle / step) * step;
                }
                RotateTargets(angle);
                _editor.StatusText = Loc.F("Rotate {0:0.##}°", -angle * 180 / Math.PI);
                break;
            }

            case InteractionMode.Scale:
            case InteractionMode.ModalScale:
            {
                var d0 = _pressScreen - _pivotScreen;
                var d1 = pos - _pivotScreen;
                double len0 = Math.Max(1, Math.Sqrt(d0.X * d0.X + d0.Y * d0.Y));
                double len1 = Math.Sqrt(d1.X * d1.X + d1.Y * d1.Y);
                double s = len1 / len0;
                double sx = s, sy = s;
                if (_mode == InteractionMode.Scale && _gizmoHandle is GizmoHandle.ScaleX or GizmoHandle.ScaleY or GizmoHandle.ScaleUniform)
                {
                    (sx, sy) = GizmoScale(pos);
                    if (!ScaleLocal(sx, sy))
                        ScaleTargets(_pivotScreen, sx, sy, ctrl);
                    _editor.StatusText = Loc.F("Scale {0:0.###} × {1:0.###}", sx, sy);
                    break;
                }
                if (_axis == 'X') sy = 1;
                else if (_axis == 'Y') sx = 1;
                else if (shift)
                {
                    sx = Math.Abs(d0.X) > 2 ? Math.Abs(d1.X / d0.X) : 1;
                    sy = Math.Abs(d0.Y) > 2 ? Math.Abs(d1.Y / d0.Y) : 1;
                }
                ScaleTargets(_pivotScreen, sx, sy, ctrl);
                _editor.StatusText = Loc.F("Scale {0:0.###} × {1:0.###}", sx, sy);
                break;
            }

            case InteractionMode.RectTransform:
                ApplyRect(pos, ctrl, shift);
                break;

            case InteractionMode.TrapezoidBase:
            {
                if (_targets.FirstOrDefault() is TrapezoidElement trapezoid)
                {
                    double slope = trapezoid.SlopeHeight;
                    double dy = (pos.Y - _pressScreen.Y) * PixelsToLevel(trapezoid);
                    trapezoid.BaseHeight = Math.Max(1, Math.Round(trapezoid.BaseHeight + dy));
                    trapezoid.Height = trapezoid.BaseHeight + slope;
                }
                break;
            }
        }
        InvalidateVisual();
    }

    private void RotateTargets(double angle)
    {
        bool single = _targets.Count == 1;
        foreach (var t in _targets)
        {
            var pivot = LevelPointFor(t, _pivotScreen);
            if (t.CanRotate || t is ObjectElement)
            {
                t.RotateAround(pivot, angle);
                continue;
            }
            if (single)
                continue; // Platforms, Trapezoids, Models... cannot rotate.

            // In a multi-selection they orbit the pivot by their center.
            var b = t.GetLocalBounds();
            var c = b.Center;
            double cos = Math.Cos(angle), sin = Math.Sin(angle);
            double dx = c.X - pivot.X, dy = c.Y - pivot.Y;
            var nc = new Point(pivot.X + dx * cos - dy * sin, pivot.Y + dx * sin + dy * cos);
            t.X += nc.X - c.X;
            t.Y += nc.Y - c.Y;
            t.ApplyIntegerLock();
        }
    }

    private void ScaleTargets(Point pivotScreen, double sx, double sy, bool free)
    {
        foreach (var t in _targets)
        {
            if (t is ModelElement && _targets.Count == 1)
                continue; // Models can only be moved.
            var pivot = LevelPointFor(t, pivotScreen);
            double tx = sx, ty = sy;
            if (t is TrapezoidElement && !free)
                ty = tx = Math.Abs(sx - 1) > Math.Abs(sy - 1) ? sx : sy; // keep the 26.565° slope
            t.ScaleAround(pivot, tx, ty);
            if (t.IntegerTransform)
            {
                t.ApplyIntegerLock();
                if (t is TrapezoidElement trapezoid && !free)
                    trapezoid.ApplySlopeAngle();
            }
        }
    }

    private void ApplyRect(Point pos, bool ctrl, bool shift)
    {
        if ((_rectHandle & RectHandle.Move) != 0)
        {
            Vector delta = new(pos.X - _pressScreen.X, pos.Y - _pressScreen.Y);
            if (shift) delta = Math.Abs(delta.X) >= Math.Abs(delta.Y) ? new Vector(delta.X, 0) : new Vector(0, delta.Y);
            foreach (var t in _targets)
            {
                double k = PixelsToLevel(t);
                var (sx0, sy0) = _memento!.StartPosition(t);
                t.X = sx0 + delta.X * k;
                t.Y = sy0 + delta.Y * k;
                if (t.IntegerTransform)
                {
                    t.X = Math.Round(t.X);
                    t.Y = Math.Round(t.Y);
                }
            }
            return;
        }

        if ((_rectHandle & RectHandle.Rotate) != 0)
        {
            double a0 = Math.Atan2(_pressScreen.Y - _pivotScreen.Y, _pressScreen.X - _pivotScreen.X);
            double a1 = Math.Atan2(pos.Y - _pivotScreen.Y, pos.X - _pivotScreen.X);
            double angle = a1 - a0;
            if (ctrl)
                angle = Math.Round(angle / (Math.PI / 12)) * (Math.PI / 12);
            RotateTargets(angle);
            return;
        }

        // Resize: the opposite edge / corner stays fixed.
        var r = _rectStart;
        double left = r.Left, right = r.Right, top = r.Top, bottom = r.Bottom;
        if ((_rectHandle & RectHandle.Left) != 0) left = Math.Min(pos.X, right - 1);
        if ((_rectHandle & RectHandle.Right) != 0) right = Math.Max(pos.X, left + 1);
        if ((_rectHandle & RectHandle.Top) != 0) top = Math.Min(pos.Y, bottom - 1);
        if ((_rectHandle & RectHandle.Bottom) != 0) bottom = Math.Max(pos.Y, top + 1);

        double sx = r.Width > 0.5 ? (right - left) / r.Width : 1;
        double sy = r.Height > 0.5 ? (bottom - top) / r.Height : 1;
        bool horizontal = (_rectHandle & (RectHandle.Left | RectHandle.Right)) != 0;
        bool vertical = (_rectHandle & (RectHandle.Top | RectHandle.Bottom)) != 0;
        if (!horizontal) sx = 1;
        if (!vertical) sy = 1;
        if (shift && horizontal && vertical)
            sx = sy = Math.Max(sx, sy);

        var anchor = new Point(
            (_rectHandle & RectHandle.Left) != 0 ? r.Right : r.Left,
            (_rectHandle & RectHandle.Top) != 0 ? r.Bottom : r.Top);

        // Shift on a side handle: uniform scale too, the other axis growing evenly around the center.
        if (shift && horizontal != vertical)
        {
            double k = horizontal ? sx : sy;
            sx = sy = k;
            anchor = horizontal ? new Point(anchor.X, r.Center.Y) : new Point(r.Center.X, anchor.Y);
        }
        ScaleTargets(anchor, sx, sy, ctrl);
    }

    private RectHandle RectZone(Point pos)
    {
        if (_editor == null || _renderer == null)
            return RectHandle.None;
        var b = _renderer.ScreenBounds(Viewport, _editor.GetTopmostSelection());
        if (b.Width <= 0 && b.Height <= 0)
            return RectHandle.None;

        const double edge = 6, corner = 9, rotate = 24;
        bool nearL = Math.Abs(pos.X - b.Left) <= edge, nearR = Math.Abs(pos.X - b.Right) <= edge;
        bool nearT = Math.Abs(pos.Y - b.Top) <= edge, nearB = Math.Abs(pos.Y - b.Bottom) <= edge;
        bool withinX = pos.X >= b.Left - edge && pos.X <= b.Right + edge;
        bool withinY = pos.Y >= b.Top - edge && pos.Y <= b.Bottom + edge;

        foreach (var (cp, h) in new[] { (b.TopLeft, RectHandle.Left | RectHandle.Top), (b.TopRight, RectHandle.Right | RectHandle.Top), (b.BottomLeft, RectHandle.Left | RectHandle.Bottom), (b.BottomRight, RectHandle.Right | RectHandle.Bottom) })
        {
            double dist = Math.Sqrt((pos.X - cp.X) * (pos.X - cp.X) + (pos.Y - cp.Y) * (pos.Y - cp.Y));
            if (dist <= corner)
                return h;
            if (dist <= rotate && !b.Contains(pos))
                return RectHandle.Rotate;
        }

        var result = RectHandle.None;
        if (nearL && withinY) result |= RectHandle.Left;
        else if (nearR && withinY) result |= RectHandle.Right;
        if (nearT && withinX) result |= RectHandle.Top;
        else if (nearB && withinX) result |= RectHandle.Bottom;
        if (result != RectHandle.None)
            return result;
        return b.Contains(pos) ? RectHandle.Move : RectHandle.None;
    }

    /// <summary>Small handle at the bottom of a single selected trapezoid that changes its base height.</summary>
    private Rect? TrapezoidHandle(SceneViewport viewport)
    {
        if (_editor == null || _renderer == null || _editor.Selection.Count != 1 || _editor.Selection[0] is not TrapezoidElement trapezoid)
            return null;
        var pts = _renderer.ScreenCorners(viewport, trapezoid);
        var bottom = new Point((pts[2].X + pts[3].X) / 2, (pts[2].Y + pts[3].Y) / 2);
        return new Rect(bottom.X - 4, bottom.Y - 4, 8, 8);
    }

    private void UpdateCursor(Point pos)
    {
        if (_editor == null)
            return;
        if (SpacePanHeld)
        {
            Cursor = HandCursor;
            return;
        }
        StandardCursorType type = StandardCursorType.Arrow;
        if (_editor.PlacementType != null || Tool == EditorTool.Brush)
            type = StandardCursorType.Cross;
        else if (_hoverHandle != GizmoHandle.None)
            type = StandardCursorType.Hand;
        else if (TrapezoidHandle(Viewport) is { } h && h.Inflate(3).Contains(pos))
            type = StandardCursorType.SizeNorthSouth;
        else if (Tool == EditorTool.Rect && _editor.HasSelection)
        {
            var zone = RectZone(pos);
            type = zone switch
            {
                RectHandle.Move => StandardCursorType.SizeAll,
                RectHandle.Rotate => StandardCursorType.Hand,
                RectHandle.Left or RectHandle.Right => StandardCursorType.SizeWestEast,
                RectHandle.Top or RectHandle.Bottom => StandardCursorType.SizeNorthSouth,
                RectHandle.Left | RectHandle.Top or RectHandle.Right | RectHandle.Bottom => StandardCursorType.TopLeftCorner,
                RectHandle.Right | RectHandle.Top or RectHandle.Left | RectHandle.Bottom => StandardCursorType.TopRightCorner,
                _ => StandardCursorType.Arrow,
            };
        }
        Cursor = type == StandardCursorType.Arrow ? null : new Cursor(type);
    }

    // ================= MODAL (G / R / S) AND STAMPING ================= //

    /// <summary>Blender-style transforms that follow the pointer until a click.</summary>
    public bool StartModal(char key)
    {
        if (_editor == null || _renderer == null || !_editor.HasSelection || _mode != InteractionMode.None)
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
