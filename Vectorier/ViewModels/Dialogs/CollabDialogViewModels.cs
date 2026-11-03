using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Services;
using Vectorier.Services.Collab;
using Vectorier.Services.Localization;

namespace Vectorier.ViewModels.Dialogs;

/// <summary>
/// Start Collaboration: the name others see, and this computer's VPN address (Radmin VPN, or Tailscale / ZeroTier)
/// that goes into the Session ID. The address and port can be changed under Advanced.
/// </summary>
public sealed partial class CollabStartViewModel : DialogViewModel
{
    public const string RadminUrl = "https://www.radmin-vpn.com/";

    public CollabStartViewModel(string userName, int port)
    {
        Title = Loc.T("Start Collaboration");
        _userName = userName;
        _portText = port.ToString();
        Detect();
    }

    public override double PreferredWidth => 500;

    /// <summary>The VPN addresses found on this computer.</summary>
    public ObservableCollection<CollabAddress> Addresses { get; } = new();

    [ObservableProperty] private string _userName;
    [ObservableProperty] private string _portText;
    [ObservableProperty] private string _addressText = string.Empty;
    [ObservableProperty] private CollabAddress? _selectedAddress;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _canStart;
    [ObservableProperty] private bool _hasVpn;
    [ObservableProperty] private bool _hasSeveral;

    partial void OnSelectedAddressChanged(CollabAddress? value)
    {
        if (value != null)
            AddressText = value.Address;
    }

    partial void OnUserNameChanged(string value) => Validate();
    partial void OnPortTextChanged(string value) => Validate();
    partial void OnAddressTextChanged(string value) => Validate();

    public int Port => int.TryParse(PortText, out int p) ? p : 0;

    /// <summary>Looks for the VPN again (after Radmin VPN was started or a network joined).</summary>
    [RelayCommand]
    private void Detect()
    {
        Addresses.Clear();
        foreach (var address in CollabNetwork.LocalAddresses().Where(a => a.IsVpn))
            Addresses.Add(address);
        HasVpn = Addresses.Count > 0;
        HasSeveral = Addresses.Count > 1;
        if (HasVpn)
            SelectedAddress = Addresses[0];
        Validate();
    }

    [RelayCommand]
    private async Task OpenRadminSite()
    {
        if (AppServices.Shell.CurrentTopLevel?.Launcher is { } launcher)
            await launcher.LaunchUriAsync(new Uri(RadminUrl));
    }

    private void Validate()
    {
        Error = string.IsNullOrWhiteSpace(UserName) ? Loc.T("Enter a username.")
            : UserName.Trim().Length > 32 ? Loc.T("The username is too long (32 characters at most).")
            : string.IsNullOrWhiteSpace(AddressText) || AddressText.Trim().Any(c => char.IsWhiteSpace(c) || c == '/') ? null
            : Port is < 1024 or > 65535 ? Loc.T("The port must be a number from 1024 to 65535.")
            : null;
        CanStart = Error == null && !string.IsNullOrWhiteSpace(AddressText) && !AddressText.Trim().Any(c => char.IsWhiteSpace(c) || c == '/');
    }

    [RelayCommand]
    private void Start()
    {
        Validate();
        if (CanStart)
            Close(true);
    }

    [RelayCommand]
    private void CancelDialog() => Close(false);
}

/// <summary>Shows the Session ID after starting (copyable).</summary>
public sealed partial class CollabCodeViewModel : DialogViewModel
{
    public CollabCodeViewModel()
    {
        Title = Loc.T("Live Collaboration");
    }

    public override double PreferredWidth => 520;

    public CollabSession Session => CollabSession.Current;

    [ObservableProperty] private string _copyText = Loc.T("Copy");

    [RelayCommand]
    private async Task Copy()
    {
        if (Session.Code is { } code)
        {
            await Services.Collab.CollabUi.CopyAsync(code, quiet: true);
            CopyText = Loc.T("Copied");
        }
    }

    [RelayCommand]
    private void Ok() => Close(true);
}

/// <summary>Joining: what is happening, with a progress bar and Cancel.</summary>
public sealed partial class CollabProgressViewModel : DialogViewModel, ICollabJoinUi
{
    private readonly CancellationTokenSource _cancel = new();

    public CollabProgressViewModel()
    {
        Title = Loc.T("Join Collaboration");
    }

    public override double PreferredWidth => 460;

    public CancellationToken Token => _cancel.Token;

    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private double _fraction;
    [ObservableProperty] private bool _isIndeterminate = true;

    public void Report(string text, double? fraction)
    {
        void Apply()
        {
            Text = text;
            IsIndeterminate = fraction == null;
            Fraction = (fraction ?? 0) * 100;
        }

        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Dispatcher.UIThread.Post(Apply);
    }

    public Task<bool> AskAsync(string title, string message, string yes, string no) =>
        AppServices.Dialogs.ConfirmAsync(title, message, yes, no, MessageKind.Warning);

    public void Finish() => Close(null);

    [RelayCommand]
    private void CancelJoin() => _cancel.Cancel();

    public override void Cancel()
    {
        // Escape / the window's close button: the join stops; the dialog closes once it has.
        if (!Result.IsCompleted)
            _cancel.Cancel();
    }
}

/// <summary>Host: who is in the session, with Kick buttons.</summary>
public sealed partial class CollabUsersViewModel : DialogViewModel
{
    public CollabUsersViewModel()
    {
        Title = Loc.T("Manage Users");
        Session.Users.CollectionChanged += OnUsersChanged;
        Rebuild();
    }

    public override double PreferredWidth => 420;

    public CollabSession Session => CollabSession.Current;

    public ObservableCollection<CollabUserRow> Rows { get; } = new();

    [ObservableProperty] private bool _isAlone;

    private void OnUsersChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        Rows.Clear();
        foreach (var user in Session.Users)
            Rows.Add(new CollabUserRow(user, Session.DisplayName(user)));
        IsAlone = Rows.Count(r => !r.User.IsHost) == 0;
    }

    public override void Cancel()
    {
        Session.Users.CollectionChanged -= OnUsersChanged;
        base.Cancel();
    }

    [RelayCommand]
    private async Task Kick(CollabUserRow? row)
    {
        var user = row?.User;
        if (user == null || user.IsHost)
            return;
        if (await AppServices.Dialogs.ConfirmAsync(Loc.T("Kick User"), Loc.F("Remove {0} from the session?", user.Name), Loc.T("Kick"), Loc.T("Cancel"), MessageKind.Warning))
            await Session.KickAsync(user.Id);
    }

    [RelayCommand]
    private void CloseDialog() => Cancel();
}

public sealed class CollabUserRow(CollabUser user, string name)
{
    public CollabUser User { get; } = user;
    public string Name { get; } = name;
    public bool CanKick => !User.IsHost;
    public Avalonia.Media.IBrush Brush { get; } = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromUInt32(user.Color | 0xFF000000));
}
