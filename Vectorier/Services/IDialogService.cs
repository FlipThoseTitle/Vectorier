using System;
using System.Threading.Tasks;
using Vectorier.ViewModels.Dialogs;

namespace Vectorier.Services;

public enum MessageKind
{
    Info,
    Warning,
    Error,
    Question
}

public enum SaveChoice
{
    Save,
    DontSave,
    Cancel
}

public interface IDialogService
{
    Task ShowMessageAsync(string title, string message, MessageKind kind = MessageKind.Info);

    Task<bool> ConfirmAsync(string title, string message, string? confirmText = null, string? cancelText = null, MessageKind kind = MessageKind.Question);

    Task<SaveChoice> AskSaveChangesAsync(string documentName);

    /// <summary>Asks for a line of text. <paramref name="validate"/> returns an error message or null.</summary>
    Task<string?> PromptAsync(string title, string label, string initialText = "", Func<string, string?>? validate = null, string? confirmText = null);

    /// <summary>Shows any dialog view model and returns the value passed to <see cref="DialogViewModel.Close"/>.</summary>
    Task<object?> ShowAsync(DialogViewModel dialog);
}
