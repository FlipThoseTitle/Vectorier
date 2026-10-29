using Avalonia.Controls;
using Avalonia.Interactivity;
using Vectorier.ViewModels.Editor;

namespace Vectorier.Views.Editor;

public partial class MoveVisualizerView : UserControl
{
    public MoveVisualizerView()
    {
        InitializeComponent();
    }

    /// <summary>A trick in the "Select Trick..." list: pick it and close the list.</summary>
    private void OnTrickPicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MoveVisualizerViewModel vm || sender is not Control { DataContext: TrickEntry trick } control)
            return;
        vm.PickTrickCommand.Execute(trick);
        SelectTrickButton.Flyout?.Hide();
    }
}
