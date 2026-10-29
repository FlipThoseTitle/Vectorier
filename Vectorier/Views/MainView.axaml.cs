using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Vectorier.ViewModels.Dialogs;

namespace Vectorier.Views;

/// <summary>
/// Single view used by the browser build. Screens replace each other in <see cref="ScreenHost"/> and
/// dialogs are shown as modal overlays (the browser has no native windows).
/// </summary>
public partial class MainView : UserControl
{
    private readonly List<(DialogViewModel Dialog, Control Layer)> _dialogs = new();

    public MainView()
    {
        InitializeComponent();
        Current = this;
        Controls.FocusOut.Attach(this);
    }

    public static MainView? Current { get; private set; }

    public object? ScreenContent
    {
        get => ScreenHost.Content;
        set => ScreenHost.Content = value;
    }

    public void PushDialog(DialogViewModel dialog)
    {
        var frame = new Border
        {
            Background = (IBrush?)this.FindResource("AppPanelBackground") ?? Brushes.White,
            BorderBrush = (IBrush?)this.FindResource("AppBorderStrong") ?? Brushes.Gray,
            BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 4 24 0 #60000000"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 320,
            MaxWidth = 1100,
            Child = new DockPanel
            {
                Children =
                {
                    new Border
                    {
                        [DockPanel.DockProperty] = Dock.Top,
                        Classes = { "panel-header" },
                        Child = new TextBlock { Text = dialog.Title, Classes = { "header" }, Margin = new Thickness(4, 0) },
                    },
                    new ContentControl { Content = dialog },
                },
            },
        };

        if (dialog.Resizable)
        {
            frame.Width = dialog.PreferredWidth;
            frame.Height = dialog.PreferredHeight;
        }

        var layer = new Panel
        {
            Background = new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)),
            Children = { frame },
        };
        layer.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && dialog.CanCancelWithEscape)
            {
                dialog.Cancel();
                e.Handled = true;
            }
        };

        _dialogs.Add((dialog, layer));
        DialogLayer.Children.Add(layer);
        DialogLayer.IsVisible = true;
    }

    public void PopDialog(DialogViewModel dialog)
    {
        var entry = _dialogs.FirstOrDefault(d => d.Dialog == dialog);
        if (entry.Layer == null)
            return;
        _dialogs.Remove(entry);
        DialogLayer.Children.Remove(entry.Layer);
        DialogLayer.IsVisible = _dialogs.Count > 0;
    }
}
