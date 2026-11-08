using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Vectorier.Services;

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

/// <summary>Browser implementation: a single view whose content is the current screen.</summary>
public sealed class SingleViewShell : IShell
{
    private readonly Views.MainView _host;

    public SingleViewShell(Views.MainView host)
    {
        _host = host;
    }

    public TopLevel? CurrentTopLevel => TopLevel.GetTopLevel(_host);
    public IScreen? CurrentScreen { get; private set; }
    public event EventHandler? ThemeChanged;
    public void RaiseThemeChanged() => ThemeChanged?.Invoke(this, EventArgs.Empty);

    public void Show(IScreen screen)
    {
        var previous = CurrentScreen;
        CurrentScreen = screen;
        _host.ScreenContent = screen;
        previous?.OnClosed();
    }
}
