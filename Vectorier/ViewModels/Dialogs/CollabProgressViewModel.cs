using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Services;
using Vectorier.Services.Collab;
using Vectorier.Services.Localization;

namespace Vectorier.ViewModels.Dialogs;

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
