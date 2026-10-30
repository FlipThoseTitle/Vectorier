using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

/// <summary>Message box, confirmation and single-line prompt.</summary>
public sealed partial class MessageDialogViewModel : DialogViewModel
{
    public MessageDialogViewModel(string title, string message, MessageKind kind)
    {
        Title = title;
        Message = message;
        Kind = kind;
    }

    public string Message { get; }
    public MessageKind Kind { get; }

    public bool IsError => Kind == MessageKind.Error;
    public bool IsWarning => Kind == MessageKind.Warning;
    public bool IsQuestion => Kind == MessageKind.Question;
    public bool IsInfo => Kind == MessageKind.Info && !ShowInput;

    public ObservableCollection<DialogButton> Buttons { get; } = new();

    public bool ShowInput { get; init; }

    /// <summary>Optional check box under the message (e.g. "Apply to all files").</summary>
    public string? CheckBoxText { get; init; }
    public bool ShowCheckBox => !string.IsNullOrEmpty(CheckBoxText);
    [ObservableProperty] private bool _isChecked;

    public Func<string, string?>? Validator { get; init; }

    [ObservableProperty] private string _inputText = string.Empty;
    [ObservableProperty] private string? _validationError;

    partial void OnInputTextChanged(string value) => Validate();

    private bool Validate()
    {
        ValidationError = ShowInput ? Validator?.Invoke(InputText) : null;
        bool valid = ValidationError == null;
        foreach (var button in Buttons)
        {
            if (button.RequiresValidInput)
                button.IsEnabled = valid;
        }
        return valid;
    }

    [RelayCommand]
    private void Press(DialogButton? button)
    {
        if (button == null)
            return;
        if (button.RequiresValidInput && !Validate())
            return;
        Close(button.Result);
    }

    public void SubmitDefault()
    {
        foreach (var button in Buttons)
        {
            if (button.IsDefault && button.IsEnabled)
            {
                Press(button);
                return;
            }
        }
    }

    public override void Cancel()
    {
        foreach (var button in Buttons)
        {
            if (button.IsCancel)
            {
                Close(button.Result);
                return;
            }
        }
        Close(null);
    }
}
