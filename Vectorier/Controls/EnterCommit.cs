using System.Linq;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Vectorier.Controls;

/// <summary>
/// Enter / Numpad Enter applies a single-line field right away (fields bound with LostFocus otherwise wait until
/// the focus leaves them).
/// </summary>
public static class EnterCommit
{
    public static void Install()
    {
        InputElement.KeyDownEvent.AddClassHandler<TextBox>(OnKeyDown, RoutingStrategies.Tunnel);
    }

    private static void OnKeyDown(TextBox box, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || box.AcceptsReturn)
            return;
        BindingOperations.GetBindingExpressionBase(box, TextBox.TextProperty)?.UpdateSource();
        // Leave the field: number fields commit their text when they lose focus, and editing ends.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var top = TopLevel.GetTopLevel(box);
            if (top?.FocusManager?.GetFocusedElement() is Avalonia.Visual focused && (ReferenceEquals(focused, box) || box.IsVisualAncestorOf(focused) || focused.IsVisualAncestorOf(box)))
            {
                // Focus the view holding the field (not the window: the editor shortcuts listen on its views).
                var host = box.GetVisualAncestors().OfType<UserControl>().FirstOrDefault();
                if (host != null)
                {
                    host.Focusable = true;
                    host.Focus();
                }
                else
                {
                    top.Focus();
                }
            }
        });
    }
}
