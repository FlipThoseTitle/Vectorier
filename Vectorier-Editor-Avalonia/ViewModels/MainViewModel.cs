using CommunityToolkit.Mvvm.ComponentModel;

namespace Vectorier_Editor_Avalonia.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Greeting { get; set; } = "Welcome to Avalonia!";
}
