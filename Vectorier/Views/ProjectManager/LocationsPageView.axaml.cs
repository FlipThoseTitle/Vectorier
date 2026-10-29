using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Vectorier.ViewModels.ProjectManager;

namespace Vectorier.Views.ProjectManager;

public partial class LocationsPageView : UserControl
{
    public LocationsPageView()
    {
        InitializeComponent();
        NameBox.LostFocus += async (_, _) => { if (Vm != null) await Vm.CommitNameAsync(); };
        NameBox.KeyDown += async (_, e) => { if (e.Key == Key.Enter && Vm != null) await Vm.CommitNameAsync(); };
        DisplayNameBox.LostFocus += async (_, _) => { if (Vm != null) await Vm.CommitDisplayNameAsync(); };
        DisplayNameBox.KeyDown += async (_, e) => { if (e.Key == Key.Enter && Vm != null) await Vm.CommitDisplayNameAsync(); };
    }

    private LocationsPageViewModel? Vm => DataContext as LocationsPageViewModel;

    private static LocationItemViewModel? LocationOf(Control control) =>
        (control.FindAncestorOfType<Expander>()?.Tag) as LocationItemViewModel;

    private void OnLocationHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm != null && sender is Control { Tag: LocationItemViewModel location })
        {
            Vm.SelectedLocation = location;
            Vm.SelectedLevel = null;
        }
    }

    private void OnLevelSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Vm != null && e.AddedItems.Count > 0 && e.AddedItems[0] is LocationLevelItem level)
            Vm.SelectedLevel = level;
    }

    private void OnLevelDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Vm?.SelectedLevel != null && (e.Source as Control)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) != null)
            Vm.OpenLevel(Vm.SelectedLevel);
    }

    private void OnAddClicked(object? sender, RoutedEventArgs e)
    {
        if (Vm == null || sender is not Button { Tag: LocationGroupItem group } button)
            return;
        var location = LocationOf(button);
        if (location == null)
            return;

        var flyout = new MenuFlyout();
        foreach (var entry in Vm.GetAddEntries(location, group))
        {
            var item = new MenuItem { Header = entry.Level == null ? "New Level" : "Move here: " + entry.Header };
            item.Click += async (_, _) => await Vm.AddToGroupAsync(location, group, entry);
            flyout.Items.Add(item);
            if (entry.Level == null && flyout.Items.Count == 1)
                flyout.Items.Add(new Separator());
        }
        flyout.ShowAt(button);
    }

    private async void OnRemoveClicked(object? sender, RoutedEventArgs e)
    {
        if (Vm?.SelectedLevel is { } level && sender is Button { Tag: LocationGroupItem group } && group.Levels.Contains(level))
            await Vm.RemoveFromGroupAsync(level);
    }

    private async void OnMoveUpClicked(object? sender, RoutedEventArgs e)
    {
        if (Vm?.SelectedLevel is { } level && sender is Button { Tag: LocationGroupItem group } && group.Levels.Contains(level))
            await Vm.MoveLevelAsync(level, -1);
    }

    private async void OnMoveDownClicked(object? sender, RoutedEventArgs e)
    {
        if (Vm?.SelectedLevel is { } level && sender is Button { Tag: LocationGroupItem group } && group.Levels.Contains(level))
            await Vm.MoveLevelAsync(level, 1);
    }
}
