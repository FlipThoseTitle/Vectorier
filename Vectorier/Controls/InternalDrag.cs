using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Vectorier.Controls;

/// <summary>A control that accepts items dragged inside the editor (Hierarchy rows, Project Window tiles).</summary>
public interface IInternalDropTarget
{
    /// <summary>Called while the pointer is over the target. Returns whether the payload can be dropped there.</summary>
    bool DragOver(object payload, Point position);

    void DragLeave();

    void Drop(object payload, Point position, KeyModifiers modifiers);
}

/// <summary>
/// Drag and drop between editor panels. Works the same on desktop and in the browser because it only uses
/// pointer events: the source keeps the pointer captured and the target under the pointer is looked up.
/// </summary>
public sealed class InternalDrag
{
    private const double Threshold = 5;

    private readonly Control _source;
    private readonly Func<object?> _payloadFactory;
    private Point _start;
    private bool _pressed;
    private object? _payload;
    private IInternalDropTarget? _hover;

    public InternalDrag(Control source, Func<object?> payloadFactory)
    {
        _source = source;
        _payloadFactory = payloadFactory;
        source.AddHandler(InputElement.PointerPressedEvent, OnPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        source.AddHandler(InputElement.PointerMovedEvent, OnMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        source.AddHandler(InputElement.PointerReleasedEvent, OnReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        source.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => Cancel(), handledEventsToo: true);
    }

    /// <summary>True while a drag is in progress (any source).</summary>
    public static bool IsDragging { get; private set; }

    /// <summary>Raised on the source when a drag starts.</summary>
    public event Action? Started;

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_source).Properties.IsLeftButtonPressed)
            return;
        _pressed = true;
        _payload = null;
        _start = e.GetPosition(_source);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (!_pressed)
            return;
        if (!e.GetCurrentPoint(_source).Properties.IsLeftButtonPressed)
        {
            Cancel();
            return;
        }

        var pos = e.GetPosition(_source);
        if (_payload == null)
        {
            if (Math.Abs(pos.X - _start.X) < Threshold && Math.Abs(pos.Y - _start.Y) < Threshold)
                return;
            _payload = _payloadFactory();
            if (_payload == null)
            {
                _pressed = false;
                return;
            }
            IsDragging = true;
            e.Pointer.Capture(_source);
            Started?.Invoke();
        }

        var target = FindTarget(e, out var targetPos);
        if (!ReferenceEquals(target, _hover))
        {
            _hover?.DragLeave();
            _hover = target;
        }
        bool accepted = target != null && target.DragOver(_payload, targetPos);
        _source.Cursor = new Cursor(accepted ? StandardCursorType.DragCopy : StandardCursorType.No);
        e.Handled = true;
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_pressed)
            return;
        var payload = _payload;
        _pressed = false;
        _payload = null;
        if (payload == null)
            return;

        var target = FindTarget(e, out var targetPos);
        _hover?.DragLeave();
        _hover = null;
        _source.Cursor = null;
        IsDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
        if (target != null && target.DragOver(payload, targetPos))
            target.Drop(payload, targetPos, e.KeyModifiers);
        target?.DragLeave();
    }

    private void Cancel()
    {
        if (_payload != null)
        {
            _hover?.DragLeave();
            _hover = null;
            _source.Cursor = null;
            IsDragging = false;
        }
        _pressed = false;
        _payload = null;
    }

    private IInternalDropTarget? FindTarget(PointerEventArgs e, out Point position)
    {
        position = default;
        var top = TopLevel.GetTopLevel(_source);
        if (top == null)
            return null;
        var hit = top.InputHitTest(e.GetPosition(top)) as Visual;
        while (hit != null)
        {
            if (hit is IInternalDropTarget target && hit is Control control)
            {
                position = e.GetPosition(control);
                return target;
            }
            hit = hit.GetVisualParent();
        }
        return null;
    }
}
