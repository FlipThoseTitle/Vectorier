using Avalonia.Controls;

namespace Vectorier.Views.Preview;

public partial class CharacterPreviewView : UserControl
{
    public CharacterPreviewView()
    {
        InitializeComponent();
        // Space plays / pauses (anywhere in the window except while typing a number).
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Space && e.Source is not TextBox && DataContext is ViewModels.Preview.CharacterPreviewViewModel vm)
            {
                vm.TogglePlayCommand.Execute(null);
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => this.FindControl<Controls.CharacterPreviewCanvas>("Canvas")?.Focus());
    }
}
