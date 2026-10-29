using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Vectorier.Controls;

/// <summary>
/// Clicking outside a text field (search bar, input field) takes the keyboard focus away from it: the clicked
/// area (or the nearest focusable control above it) gets the focus, so shortcuts such as Delete reach the
/// selected item instead of the text field. Attached once per window / root view.
/// </summary>
public static class FocusOut
{
    public static void Attach(Control root)
    {
        root.Focusable = true;
        root.AddHandler(InputElement.PointerPressedEvent, (_, e) => OnPressed(root, e), RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private static void OnPressed(Control root, PointerPressedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(root);
        if (top?.FocusManager?.GetFocusedElement() is not TextBox focused)
            return;
        var source = e.Source as Visual;
        // Another text field, or the field itself: normal focus handling.
        if (source == null || ReferenceEquals(source, focused) || source.FindAncestorOfType<TextBox>(true) != null)
            return;
        if (source.FindAncestorOfType<NumericUpDown>(true) != null || source.FindAncestorOfType<ComboBox>(true) != null)
            return;

        for (var v = source; v != null; v = v.GetVisualParent())
        {
            if (v is InputElement { Focusable: true, IsEffectivelyEnabled: true, IsEffectivelyVisible: true } target && !ReferenceEquals(target, focused))
            {
                target.Focus();
                return;
            }
        }
        // Nothing focusable under the pointer: just leave the text field.
        root.Focus();
    }
}
