using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Vectorier_Editor_Avalonia.ViewModels;

namespace Vectorier_Editor_Avalonia.Services;

/// <summary>A top-level screen (Project Selection, Project Manager, Level Editor...).</summary>
public interface IScreen
{
    string Title { get; }

    /// <summary>Preferred window size on the desktop.</summary>
    Size PreferredSize { get; }

    bool StartMaximized { get; }

    /// <summary>Called before the screen is closed by the user; return false to keep it open.</summary>
    Task<bool> CanCloseAsync();

    void OnClosed();
}

/// <summary>Switches between screens. Desktop: one window per screen. Browser: one view whose content changes.</summary>
public interface IShell
{
    void Show(IScreen screen);

    /// <summary>The top level hosting the active screen (for pickers, clipboard and dialogs).</summary>
    TopLevel? CurrentTopLevel { get; }

    IScreen? CurrentScreen { get; }

    /// <summary>Raised when the theme changes so custom-drawn controls can refresh.</summary>
    event EventHandler? ThemeChanged;

    void RaiseThemeChanged();
}

/// <summary>Desktop implementation: every screen gets its own window, the previous window closes.</summary>
public sealed class DesktopShell : IShell
{
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private ShellWindow? _current;

    public DesktopShell(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _lifetime = lifetime;
    }

    public TopLevel? CurrentTopLevel => _current;
    public IScreen? CurrentScreen => _current?.Screen;
    public event EventHandler? ThemeChanged;
    public void RaiseThemeChanged() => ThemeChanged?.Invoke(this, EventArgs.Empty);

    public void Show(IScreen screen)
    {
        var previous = _current;
        var window = new ShellWindow(screen);

        if (previous != null && !screen.StartMaximized && previous.WindowState != WindowState.Maximized)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            var center = previous.Position + new PixelPoint((int)(previous.Bounds.Width / 2), (int)(previous.Bounds.Height / 2));
            window.Position = center - new PixelPoint((int)(screen.PreferredSize.Width / 2), (int)(screen.PreferredSize.Height / 2));
        }

        _current = window;
        _lifetime.MainWindow = window;
        window.Show();

        if (previous != null)
            previous.CloseWithoutPrompt();
    }
}

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
            using var stream = AssetLoader.Open(new Uri("avares://Vectorier-Editor-Avalonia/Assets/vectorier.ico"));
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

/// <summary>Browser implementation: a single view whose content is the current screen.</summary>
public sealed class SingleViewShell : IShell
{
    private readonly Views.MainView _host;
    private IScreen? _current;

    public SingleViewShell(Views.MainView host)
    {
        _host = host;
    }

    public TopLevel? CurrentTopLevel => TopLevel.GetTopLevel(_host);
    public IScreen? CurrentScreen => _current;
    public event EventHandler? ThemeChanged;
    public void RaiseThemeChanged() => ThemeChanged?.Invoke(this, EventArgs.Empty);

    public void Show(IScreen screen)
    {
        var previous = _current;
        _current = screen;
        _host.ScreenContent = screen;
        previous?.OnClosed();
    }
}
