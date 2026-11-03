using System;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Vectorier.Services.Localization;
using Vectorier.ViewModels.Dialogs;
using Vectorier.ViewModels.Editor;
using Vectorier.ViewModels.ProjectManager;

namespace Vectorier.Services.Collab;

/// <summary>The Live Collaboration prompts: starting, joining, leaving, managing users, the session closing.</summary>
public static class CollabUi
{
    private const string KeyUserName = "Collab.UserName";
    private const string KeyPort = "Collab.Port";

    private static CollabSession Session => CollabSession.Current;

    static CollabUi()
    {
        Session.Ended += (reason, editor) => Dispatcher.UIThread.Post(() => _ = OnEndedAsync(reason, editor));
        Session.Notice += text =>
        {
            if (Session.Editor is { } editor)
                editor.StatusText = text;
        };
    }

    /// <summary>Hooks the session's events up (call once at startup).</summary>
    public static void Initialize()
    {
    }

    private static string RememberedName =>
        AppServices.Platform?.Settings.Get(KeyUserName) is { Length: > 0 } name ? name : Environment.UserName;

    private static int RememberedPort =>
        int.TryParse(AppServices.Platform?.Settings.Get(KeyPort), out int p) && p is >= 1024 and <= 65535 ? p : CollabNetwork.DefaultPort;

    private static void Remember(string name, int? port = null)
    {
        AppServices.Platform?.Settings.Set(KeyUserName, name);
        if (port is { } p)
            AppServices.Platform?.Settings.Set(KeyPort, p.ToString());
    }

    private static string? ValidateName(string text) =>
        string.IsNullOrWhiteSpace(text) ? Loc.T("Enter a username.")
        : text.Trim().Length > 32 ? Loc.T("The username is too long (32 characters at most).")
        : null;

    // ================= START ================= //

    public static async Task StartAsync(EditorViewModel editor)
    {
        if (Session.IsActive)
            return;
        if (!editor.CanCollaborate)
        {
            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Live Collaboration"), Loc.T("Leave Edit Object first, then start Live Collaboration."));
            return;
        }
        if (editor.Document.FilePath == null || editor.Document.ExternalFile != null)
        {
            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Live Collaboration"), Loc.T("Save the level in its project first, then start Live Collaboration."));
            return;
        }
        var dialog = new CollabStartViewModel(RememberedName, RememberedPort);
        if (await AppServices.Dialogs.ShowAsync(dialog) is not true)
            return;
        string name = dialog.UserName.Trim();
        Remember(name, dialog.Port);
        try
        {
            editor.IsLoading = true;
            await Session.StartAsync(editor, name, dialog.AddressText.Trim(), dialog.Port);
        }
        catch (Exception ex)
        {
            editor.IsLoading = false;
            string message = ex is System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.AddressAlreadyInUse }
                ? Loc.F("Port {0} is already used by another program. Choose another port.", dialog.Port)
                : ex.Message;
            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Start Collaboration"), Loc.T("The session could not be started:") + "\n\n" + message, MessageKind.Error);
            return;
        }
        editor.IsLoading = false;
        await AppServices.Dialogs.ShowAsync(new CollabCodeViewModel());
    }

    // ================= JOIN ================= //

    public static async Task JoinAsync(EditorViewModel editor)
    {
        if (Session.IsActive)
            return;
        string? text = await AppServices.Dialogs.PromptAsync(Loc.T("Join Collaboration"), Loc.T("Session ID from the host (you need to be in the same Radmin VPN network as the host):"), string.Empty,
            t => CollabCode.TryParse(t) == null ? (string.IsNullOrWhiteSpace(t) ? Loc.T("Paste the Session ID.") : Loc.T("This is not a valid Session ID.")) : null,
            Loc.T("Next"));
        if (text == null || CollabCode.TryParse(text) is not { } code)
            return;
        string? name = await AppServices.Dialogs.PromptAsync(Loc.T("Join Collaboration"), Loc.T("Username (others see it next to your cursor):"), RememberedName, ValidateName, Loc.T("Join"));
        if (name == null)
            return;
        Remember(name);

        // The shared level opens in place of this one.
        if (!await editor.CanCloseAsync())
            return;
        editor.CloseForCollabEnd();

        var progress = new CollabProgressViewModel();
        var shown = AppServices.Dialogs.ShowAsync(progress);
        string? problem;
        try
        {
            problem = await Session.JoinAsync(code, name, progress, progress.Token);
        }
        catch (Exception ex)
        {
            problem = ex.Message;
        }
        progress.Close(null);
        await shown;
        if (problem != null)
        {
            editor.CloseForCollabEnd(false);
            if (problem != Loc.T("Cancelled."))
                await AppServices.Dialogs.ShowMessageAsync(Loc.T("Join Collaboration"), problem, MessageKind.Error);
        }
    }

    // ================= LEAVE / STOP ================= //

    /// <summary>Stop Collaboration (host) / Leave Collaboration (participant), after confirming.</summary>
    public static async Task ConfirmAndEndAsync(EditorViewModel editor)
    {
        if (!Session.IsActive)
            return;
        bool host = Session.IsHost;
        bool ok = host
            ? await AppServices.Dialogs.ConfirmAsync(Loc.T("Stop Collaboration"),
                Loc.T("Stopping the session disconnects everyone. The level stays open here."), Loc.T("Stop"), Loc.T("Cancel"), MessageKind.Warning)
            : await AppServices.Dialogs.ConfirmAsync(Loc.T("Leave Collaboration"),
                Loc.T("Leave the session? Your copy of the project and the level stay on this computer."), Loc.T("Leave"), Loc.T("Cancel"), MessageKind.Warning);
        if (ok)
        {
            await Session.EndAsync();
            editor.StatusText = host ? Loc.T("Live Collaboration stopped.") : Loc.T("You left the session.");
        }
    }

    /// <summary>Before the shared level is left (Project Manager, New Level, Open, closing the window). False: stay.</summary>
    public static async Task<bool> ConfirmLeaveLevelAsync(EditorViewModel editor)
    {
        if (!Session.IsActive || !ReferenceEquals(Session.Editor, editor))
            return true;
        bool ok = Session.IsHost
            ? await AppServices.Dialogs.ConfirmAsync(Loc.T("Live Collaboration"),
                Loc.T("You are hosting a Live Collaboration session. Leaving this level stops it and disconnects everyone."), Loc.T("Stop and Leave"), Loc.T("Cancel"), MessageKind.Warning)
            : await AppServices.Dialogs.ConfirmAsync(Loc.T("Live Collaboration"),
                Loc.T("Leaving this level also leaves the Live Collaboration session."), Loc.T("Leave"), Loc.T("Cancel"), MessageKind.Warning);
        if (ok)
            await Session.EndAsync();
        return ok;
    }

    /// <summary>Participant: the host closed the session, kicked us, or the connection was lost.</summary>
    private static async Task OnEndedAsync(string reason, EditorViewModel? editor)
    {
        await AppServices.Dialogs.ShowMessageAsync(Loc.T("Session Closed"), reason, MessageKind.Warning);
        if (editor == null || !ReferenceEquals(AppServices.Shell.CurrentScreen, editor))
            return;
        if (editor.IsDirty)
        {
            // Unsaved shared changes: the usual Save / Don't Save / Cancel. Cancel keeps the level open (offline).
            if (!await editor.CanCloseAsync())
                return;
        }
        editor.CloseForCollabEnd();
        AppServices.Shell.Show(new ProjectManagerViewModel(editor.Project));
    }

    // ================= USERS / CLIPBOARD ================= //

    public static async Task ManageUsersAsync()
    {
        if (Session.IsHost)
            await AppServices.Dialogs.ShowAsync(new CollabUsersViewModel());
    }

    public static async Task CopyAsync(string text, bool quiet = false)
    {
        var clipboard = AppServices.Shell.CurrentTopLevel?.Clipboard;
        if (clipboard == null)
            return;
        await clipboard.SetTextAsync(text);
        if (!quiet && Session.Editor is { } editor)
            editor.StatusText = Loc.T("Session ID copied.");
    }
}
