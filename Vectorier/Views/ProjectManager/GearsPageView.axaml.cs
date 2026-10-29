using Avalonia.Controls;
using Avalonia.Input;
using Vectorier.ViewModels.ProjectManager;

namespace Vectorier.Views.ProjectManager;

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
