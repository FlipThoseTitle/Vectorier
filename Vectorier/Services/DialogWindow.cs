using System;
using Avalonia.Controls;
using Avalonia.Input;
using Vectorier.ViewModels.Dialogs;

namespace Vectorier.Services;

/// <summary>Modal window used for dialogs on the desktop.</summary>
public sealed class DialogWindow : Window
{
    private readonly DialogViewModel _dialog;

    public DialogWindow(DialogViewModel dialog)
    {
        _dialog = dialog;
        Title = dialog.Title;
        Content = dialog;
        SizeToContent = dialog.Resizable ? SizeToContent.Manual : SizeToContent.WidthAndHeight;
        if (dialog.Resizable)
        {
            Width = dialog.PreferredWidth;
            Height = dialog.PreferredHeight;
        }
        CanResize = dialog.Resizable;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = ShellWindow.LoadIcon();
        MinWidth = 320;

        dialog.CloseRequested += Close;
        KeyDown += OnKeyDown;
        Controls.FocusOut.Attach(this);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _dialog.CanCancelWithEscape)
        {
            _dialog.Cancel();
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _dialog.CloseRequested -= Close;
        _dialog.Cancel();
    }
}
