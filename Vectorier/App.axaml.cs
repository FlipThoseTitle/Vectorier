using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Vectorier.Services;
using Vectorier.ViewModels;
using Vectorier.Views;

namespace Vectorier;

public partial class App : Application
{
    public const string ThemeSettingKey = "Theme";

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Views.ViewRegistry.RegisterAll();
        Controls.NumberDrag.Install();
        Controls.EnterCommit.Install();
        AppServices.Dialogs = new DialogService();
        ApplyTheme(AppServices.Platform?.Settings.Get(ThemeSettingKey));

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnLastWindowClose;
            AppServices.Shell = new DesktopShell(desktop);
            StartAsync();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            var host = new MainView();
            singleView.MainView = host;
            AppServices.Shell = new SingleViewShell(host);
            StartAsync();
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activity)
        {
            activity.MainViewFactory = () =>
            {
                var host = new MainView();
                AppServices.Shell = new SingleViewShell(host);
                StartAsync();
                return host;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async void StartAsync()
    {
        try
        {
            var fs = await AppServices.Platform.TryResolveRootAsync();
            if (fs != null)
            {
                AppServices.SetFileSystem(fs);
                AppServices.Shell.Show(new ProjectSelectionViewModel());
            }
            else
            {
                AppServices.Shell.Show(new RootPickerViewModel());
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            AppServices.Shell.Show(new RootPickerViewModel());
        }
    }

    /// <summary>Light is the default; "Dark" switches the whole application.</summary>
    public static void ApplyTheme(string? theme)
    {
        if (Current == null)
            return;
        Current.RequestedThemeVariant = string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase)
            ? ThemeVariant.Dark
            : ThemeVariant.Light;
        AppServices.Shell?.RaiseThemeChanged();
    }

    public static bool IsDarkTheme => Current?.ActualThemeVariant == ThemeVariant.Dark;
}
