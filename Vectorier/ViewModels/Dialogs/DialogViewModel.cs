using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Vectorier.Services;

namespace Vectorier.ViewModels.Dialogs;

/// <summary>Base class for dialogs shown through <see cref="IDialogService"/>.</summary>
public abstract partial class DialogViewModel : ViewModelBase
{
    private readonly TaskCompletionSource<object?> _completion = new();

    public string Title { get; protected set; } = "Vectorier";

    public virtual bool Resizable => false;
    public virtual double PreferredWidth => 480;
    public virtual double PreferredHeight => 360;
    public virtual bool CanCancelWithEscape => true;

    public Task<object?> Result => _completion.Task;

    public event Action? CloseRequested;

    public void Close(object? result)
    {
        if (_completion.TrySetResult(result))
            CloseRequested?.Invoke();
    }

    /// <summary>Closes without a result (Escape, window close button).</summary>
    public virtual void Cancel() => Close(null);
}

public sealed partial class DialogButton : ObservableObject
{
    public DialogButton(string text, object? result, bool isDefault = false, bool isCancel = false)
    {
        Text = text;
        Result = result;
        IsDefault = isDefault;
        IsCancel = isCancel;
    }

    public string Text { get; }
    public object? Result { get; }
    public bool IsDefault { get; }
    public bool IsCancel { get; }
    public bool RequiresValidInput { get; init; }

    [ObservableProperty] private bool _isEnabled = true;
}
