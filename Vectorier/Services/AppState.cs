namespace Vectorier.Services;

/// <summary>
/// Global facts about this run of the app, readable from anywhere (e.g. <c>AppState.IsFirstLaunch</c>).
/// </summary>
public static class AppState
{
    private const string LaunchedKey = "App.HasLaunched";

    /// <summary>
    /// True when Vectorier has never been opened before on this computer (or browser): first-time setup such as
    /// picking the language from the system happens then. Stays true for the whole first session.
    /// </summary>
    public static bool IsFirstLaunch { get; private set; }

    private static bool _initialized;

    /// <summary>Called once at start-up, after the settings store is available.</summary>
    public static void Initialize()
    {
        if (_initialized)
            return;
        _initialized = true;
        var settings = AppServices.Platform?.Settings;
        IsFirstLaunch = settings?.Get(LaunchedKey) != "1";
        settings?.Set(LaunchedKey, "1");
    }
}
