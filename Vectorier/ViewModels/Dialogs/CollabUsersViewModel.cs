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
