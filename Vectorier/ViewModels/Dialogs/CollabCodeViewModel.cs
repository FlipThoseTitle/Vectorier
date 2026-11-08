using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Services.Collab;
using Vectorier.Services.Localization;

namespace Vectorier.ViewModels.Dialogs;

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
