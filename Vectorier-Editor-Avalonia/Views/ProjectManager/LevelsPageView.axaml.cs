using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Vectorier_Editor_Avalonia.ViewModels.ProjectManager;

namespace Vectorier_Editor_Avalonia.Views.ProjectManager;

public partial class LevelsPageView : UserControl
{
    public LevelsPageView()
    {
        InitializeComponent();
        LevelList.DoubleTapped += (_, e) =>
        {
            if (DataContext is LevelsPageViewModel vm && vm.SelectedItem != null &&
                (e.Source as Control)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) != null)
                vm.OpenInEditor();
        };
        DisplayNameBox.LostFocus += async (_, _) => { if (DataContext is LevelsPageViewModel vm) await vm.CommitDisplayNameAsync(); };
        DisplayNameBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && DataContext is LevelsPageViewModel vm)
                await vm.CommitDisplayNameAsync();
        };
    }
}
