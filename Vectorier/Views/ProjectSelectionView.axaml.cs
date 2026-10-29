using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Vectorier.ViewModels;

namespace Vectorier.Views;

public partial class ProjectSelectionView : UserControl
{
    public ProjectSelectionView()
    {
        InitializeComponent();
        Focusable = true;

        NameBox.LostFocus += async (_, _) => { if (Vm != null) await Vm.CommitNameAsync(); };
        NameBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && Vm != null)
            {
                e.Handled = true;
                await Vm.CommitNameAsync();
            }
        };
        AuthorBox.LostFocus += async (_, _) => { if (Vm != null) await Vm.CommitAuthorAsync(); };
        AuthorBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && Vm != null)
            {
                e.Handled = true;
                await Vm.CommitAuthorAsync();
            }
        };
        DescriptionBox.LostFocus += async (_, _) => { if (Vm != null) await Vm.CommitAuthorAsync(); };

        ProjectListBox.DoubleTapped += async (_, e) =>
        {
            if (Vm?.SelectedProject != null && (e.Source as Control)?.FindAncestorOfType<ListBoxItem>() != null)
                await Vm.OpenProjectAsync();
        };

        // Clicking empty space (outside project entries and controls) clears the selection.
        // The right panel never clears it: the user may only want to leave a text field.
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
    }

    private ProjectSelectionViewModel? Vm => DataContext as ProjectSelectionViewModel;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm == null || e.Source is not Control source)
            return;

        if (source.FindAncestorOfType<ListBoxItem>(includeSelf: true) != null)
            return;
        if (source.FindAncestorOfType<Button>(includeSelf: true) != null)
            return;
        if (source.FindAncestorOfType<TextBox>(includeSelf: true) != null)
            return;
        if (source.FindAncestorOfType<ScrollBar>(includeSelf: true) != null)
            return;
        if (IsInside(source, DetailsPanel))
        {
            // Leaving a field: just take focus away so edits are committed.
            Focus();
            return;
        }

        Focus();
        Vm.SelectedProject = null;
    }

    private static bool IsInside(Control control, Control container)
    {
        for (Control? current = control; current != null; current = current.Parent as Control)
        {
            if (ReferenceEquals(current, container))
                return true;
        }
        return false;
    }
}
