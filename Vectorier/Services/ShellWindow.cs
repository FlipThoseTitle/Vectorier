using System;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Vectorier.Services;

/// <summary>Window hosting one screen on the desktop.</summary>
public sealed class ShellWindow : Window
{
    private bool _allowClose;

    public ShellWindow(IScreen screen)
    {
        Screen = screen;
        Title = screen.Title;
        Width = screen.PreferredSize.Width;
        Height = screen.PreferredSize.Height;
        MinWidth = 640;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (screen.StartMaximized)
            WindowState = WindowState.Maximized;
        Content = screen;
        Icon = LoadIcon();
        Controls.FocusOut.Attach(this);

        if (screen is System.ComponentModel.INotifyPropertyChanged notify)
        {
            notify.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(IScreen.Title))
                    Title = screen.Title;
            };
        }
    }

    public IScreen Screen { get; }

    internal static WindowIcon? LoadIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://Vectorier.Core/Assets/vectorier.ico"));
            return new WindowIcon(stream);
        }
        catch
        {
            return null;
        }
    }

    public void CloseWithoutPrompt()
    {
        _allowClose = true;
        Close();
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_allowClose)
            return;

        e.Cancel = true;
        if (await Screen.CanCloseAsync())
        {
            _allowClose = true;
            Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Screen.OnClosed();
    }
}
