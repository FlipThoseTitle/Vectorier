using Avalonia.Controls;
using Avalonia.Input;
using Vectorier_Editor_Avalonia.ViewModels.ProjectManager;

namespace Vectorier_Editor_Avalonia.Views.ProjectManager;

public partial class GearsPageView : UserControl
{
    public GearsPageView()
    {
        InitializeComponent();
        NameBox.LostFocus += async (_, _) => { if (DataContext is ShopPageViewModel vm) await vm.CommitNameAsync(); };
        NameBox.KeyDown += async (_, e) => { if (e.Key == Key.Enter && DataContext is ShopPageViewModel vm) await vm.CommitNameAsync(); };
        DisplayNameBox.LostFocus += async (_, _) => { if (DataContext is ShopPageViewModel vm) await vm.CommitDetailsAsync(); };
        DisplayNameBox.KeyDown += async (_, e) => { if (e.Key == Key.Enter && DataContext is ShopPageViewModel vm) await vm.CommitDetailsAsync(); };
    }
}
