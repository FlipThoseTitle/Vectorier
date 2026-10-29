using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Vectorier.ViewModels.ProjectManager;

namespace Vectorier.Views.ProjectManager;

public partial class ProjectManagerView : UserControl
{
    public ProjectManagerView()
    {
        InitializeComponent();
        // Delete removes the selection of the current category (unless a text field is being edited).
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || e.KeyModifiers != KeyModifiers.None || DataContext is not ProjectManagerViewModel vm)
            return;
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox)
            return;
        if (vm.SelectedPage?.DeleteShortcut is { } command && command.CanExecute(null))
        {
            command.Execute(null);
            e.Handled = true;
        }
    }
}
