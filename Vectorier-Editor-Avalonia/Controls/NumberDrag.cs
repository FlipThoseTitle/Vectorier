using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Vectorier_Editor_Avalonia.Controls;

/// <summary>
/// Unity-like number scrubbing: dragging the label in front of a number field changes its value
/// (Shift for bigger steps, Ctrl for finer ones). Applies to every TextBlock directly followed by a NumericUpDown in
/// the same panel, so every form of the app gets it without extra markup.
/// </summary>
public static class NumberDrag
{
    private static NumericUpDown? _target;
    private static TextBlock? _label;
    private static Point _last;
    private static decimal _accumulated;
    private static bool _moved;
    private static readonly Cursor ScrubCursor = new(StandardCursorType.SizeWestEast);

    public static void Install()
    {
        InputElement.PointerEnteredEvent.AddClassHandler<TextBlock>((t, _) =>
        {
            if (FieldFor(t) is { IsEnabled: true })
                t.Cursor = ScrubCursor;
        });
        InputElement.PointerPressedEvent.AddClassHandler<TextBlock>(OnPressed, RoutingStrategies.Bubble, handledEventsToo: false);
        InputElement.PointerMovedEvent.AddClassHandler<TextBlock>(OnMoved);
        InputElement.PointerReleasedEvent.AddClassHandler<TextBlock>(OnReleased);
        InputElement.PointerCaptureLostEvent.AddClassHandler<TextBlock>((_, _) => _target = null);
    }

    /// <summary>The number field this label belongs to: the next sibling in its panel.</summary>
    private static NumericUpDown? FieldFor(TextBlock label)
    {
        if (label.Parent is not Panel panel)
            return null;
        int index = panel.Children.IndexOf(label);
        if (index < 0 || index + 1 >= panel.Children.Count)
            return null;
        return panel.Children[index + 1] as NumericUpDown;
    }

    private static void OnPressed(TextBlock label, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(label).Properties.IsLeftButtonPressed)
            return;
        var field = FieldFor(label);
        if (field == null || !field.IsEffectivelyEnabled || field.IsReadOnly)
            return;
        _target = field;
        _label = label;
        _last = e.GetPosition(null);
        _accumulated = 0;
        _moved = false;
        e.Pointer.Capture(label);
        e.Handled = true;
    }

    private static void OnMoved(TextBlock label, PointerEventArgs e)
    {
        if (_target == null || !ReferenceEquals(label, _label))
            return;
        var pos = e.GetPosition(null);
        double dx = pos.X - _last.X;
        _last = pos;
        if (Math.Abs(dx) < 1e-9)
            return;
        _moved = true;

        decimal step = _target.Increment <= 0 ? 1 : _target.Increment;
        var mods = e.KeyModifiers;
        if ((mods & KeyModifiers.Shift) != 0) step *= 10;
        else if ((mods & (KeyModifiers.Control | KeyModifiers.Meta)) != 0) step /= 10;

        _accumulated += (decimal)dx * step;
        // Whole fields (step 1 or more) only move by whole steps.
        decimal applied = step >= 1 ? Math.Truncate(_accumulated / step) * step : _accumulated;
        if (applied == 0)
            return;
        _accumulated -= applied;
        decimal value = (_target.Value ?? 0) + applied;
        value = Math.Clamp(value, _target.Minimum, _target.Maximum);
        _target.Value = value;
        e.Handled = true;
    }

    private static void OnReleased(TextBlock label, PointerReleasedEventArgs e)
    {
        if (_target == null || !ReferenceEquals(label, _label))
            return;
        e.Pointer.Capture(null);
        if (!_moved)
            _target.Focus(); // a click on the label focuses the field
        _target = null;
        _label = null;
        e.Handled = true;
    }
}
