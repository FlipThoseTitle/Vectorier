using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
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
