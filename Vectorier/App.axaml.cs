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
        Controls.MathFields.Install();
        AppServices.Dialogs = new DialogService();
        // The game's error and crash logs (Editor/Logs), and the application's unhandled errors logged as crashes.
        Services.Game.GameErrorLogs.Install();
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += OnUnhandledException;
        // First launch flag, then the language (the computer's on first launch, the saved one afterwards).
        AppState.Initialize();
        Services.Localization.Loc.Initialize();
        Services.Collab.CollabUi.Initialize();
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
                Services.Tutorial.TutorialGuide.OfferOnFirstLaunch();
                Services.UpdateChecker.CheckOnStartup();
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

    /// <summary>
    /// An error nothing caught on the editor's thread: it is written to the crash log first. One thrown by the game
    /// (its code in the stack) while a level plays does not end Vectorier: the game stops and the report is shown.
    /// Any other is left to end the application as before, with its crash log entry written.
    /// </summary>
    private static void OnUnhandledException(object? sender, Avalonia.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        bool fromGame = Services.Game.GameHost.Current.IsRunning
            && e.Exception.StackTrace?.Contains("Vectorier.Gameplay.", StringComparison.Ordinal) == true;
        var report = Gameplay.Diagnostics.GameLog.Crash(e.Exception, fromGame ? "Running the game (unhandled error)" : "Vectorier (unhandled error)");
        if (!fromGame)
            return;
        e.Handled = true;
        try
        {
            Services.Game.GameHost.Current.Stop();
        }
        catch (Exception ex)
        {
            Console.WriteLine("The game could not stop cleanly: " + ex.Message);
        }
        _ = AppServices.Dialogs.ShowAsync(new ViewModels.Dialogs.GameErrorLogViewModel(
            Services.Localization.Loc.T("Play Level"), Services.Localization.Loc.T("The game stopped because of an error."), report));
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
}
