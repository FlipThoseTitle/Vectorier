using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Services;
using Vectorier.Services.Game;
using Vectorier.Services.Localization;

namespace Vectorier.ViewModels.Editor;

/// <summary>
/// The game inside the editor: Play, Hunter Mode and Play Here switch the center panel to the Game View and play the
/// level there (see <see cref="GameHost"/>). Leaving the Game View (or Stop) ends the game.
/// </summary>
public partial class EditorViewModel
{
    /// <summary>A level is being played in the Game View.</summary>
    [ObservableProperty] private bool _isGameRunning;

    /// <summary>The Game View fills the editor's window (the game's F11).</summary>
    [ObservableProperty] private bool _isGameFullScreen;

    private bool _gameHooked;

    private void HookGame()
    {
        if (_gameHooked)
            return;
        _gameHooked = true;
        GameHost.Current.RunningChanged += OnGameRunningChanged;
        GameHost.Current.FullScreenToggled += OnGameFullScreenToggled;
        GameHost.Current.Failed += OnGameFailed;
        VectorierGameHooks.Install(this);
    }

    private void OnGameRunningChanged()
    {
        IsGameRunning = GameHost.Current.IsRunning;
        if (!IsGameRunning)
        {
            IsGameFullScreen = false;
            // Stopped: back to the Scene View, where the work goes on (and the tutorial points).
            if (_startingGame)
                return; // played again: the previous game stops, the Game View stays
            if (CenterTab == CenterTab.Game)
                CenterTab = CenterTab.Scene;
        }
    }

    private bool _startingGame;

    // Raised while the Game View draws: the message waits until the drawing is done.
    private void OnGameFailed(string message) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        _ = AppServices.Dialogs.ShowMessageAsync(Loc.T("Play Level"), Loc.T("The game stopped because of an error:\n") + message, MessageKind.Error));

    private void OnGameFullScreenToggled() => IsGameFullScreen = IsGameRunning && !IsGameFullScreen;

    partial void OnIsGameFullScreenChanged(bool value) => GameHost.Current.IsFullScreen = value;

    /// <summary>Plays <paramref name="levelXml"/> in the Game View. False (after telling why) when it cannot start.</summary>
    private async Task<bool> StartGameAsync(string levelName, byte[] levelXml, bool hunter, string title)
    {
        HookGame();
        MoveVisualizer.StopPlacement();
        CenterTab = CenterTab.Game;
        StatusText = Loc.F("Loading {0}...", levelName);
        string? error;
        _startingGame = true;
        try
        {
            error = await GameHost.Current.StartAsync(Project, levelName, levelXml, hunter);
        }
        finally
        {
            _startingGame = false;
        }
        if (error != null)
        {
            StatusText = string.Empty;
            if (CenterTab == CenterTab.Game)
                CenterTab = CenterTab.Scene;
            await AppServices.Dialogs.ShowMessageAsync(title, Loc.T("The level could not be played:\n") + error, MessageKind.Error);
            return false;
        }
        Services.Tutorial.TutorialSignals.GameLaunches++;
        GameViewFocusRequested?.Invoke();
        return true;
    }

    /// <summary>Asks the Game View to take the keyboard (the game's keys).</summary>
    public event Action? GameViewFocusRequested;

    /// <summary>Ends the game; the Game View shows the level's camera again.</summary>
    [RelayCommand]
    private void StopGame() => GameHost.Current.Stop();

    /// <summary>Called when the center tab changes: leaving the Game View ends the game.</summary>
    private void OnCenterTabChangedForGame(CenterTab oldValue, CenterTab newValue)
    {
        if (oldValue == CenterTab.Game && newValue != CenterTab.Game && GameHost.Current.IsRunning)
            GameHost.Current.Stop();
    }
}

/// <summary>The game's buttons that need the editor (Reload Moves reads the files again first).</summary>
internal static class VectorierGameHooks
{
    private static EditorViewModel? _editor;

    public static void Install(EditorViewModel editor)
    {
        _editor = editor;
        Gameplay.VectorGame.ReloadMovesRequested = () =>
        {
            // Clicked during the game's frame: done after it.
            if (_editor is { } editor)
                Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = GameHost.Current.ReloadMovesAsync(editor.Project));
        };
    }
}
