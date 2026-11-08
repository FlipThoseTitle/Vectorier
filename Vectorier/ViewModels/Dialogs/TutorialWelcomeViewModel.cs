using CommunityToolkit.Mvvm.Input;

namespace Vectorier.ViewModels.Dialogs;

/// <summary>
/// The tutorial's welcome window (first launch, Help › Tutorial): Continue starts the tutorial (result true),
/// Skip Tutorial closes it (result false).
/// </summary>
public sealed partial class TutorialWelcomeViewModel : DialogViewModel
{
    public TutorialWelcomeViewModel()
    {
        Title = Services.Localization.Loc.T("Welcome to Vectorier");
    }

    public override bool CanCancelWithEscape => false;

    [RelayCommand]
    private void Continue() => Close(true);

    [RelayCommand]
    private void Skip() => Close(false);
}
