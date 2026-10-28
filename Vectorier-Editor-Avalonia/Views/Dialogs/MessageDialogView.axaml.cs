using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Vectorier_Editor_Avalonia.ViewModels.Dialogs;

namespace Vectorier_Editor_Avalonia.Views.Dialogs;

public partial class MessageDialogView : UserControl
{
    public MessageDialogView()
    {
        InitializeComponent();
        Input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && DataContext is MessageDialogViewModel vm)
            {
                vm.SubmitDefault();
                e.Handled = true;
            }
        };
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (Input.IsVisible)
        {
            Input.Focus();
            Input.SelectAll();
        }
    }
}
