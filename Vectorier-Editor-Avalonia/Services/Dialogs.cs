using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Vectorier_Editor_Avalonia.ViewModels;
using Vectorier_Editor_Avalonia.ViewModels.Dialogs;

namespace Vectorier_Editor_Avalonia.Services;

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

    Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", string cancelText = "Cancel", MessageKind kind = MessageKind.Question);

    Task<SaveChoice> AskSaveChangesAsync(string documentName);

    /// <summary>Asks for a line of text. <paramref name="validate"/> returns an error message or null.</summary>
    Task<string?> PromptAsync(string title, string label, string initialText = "", Func<string, string?>? validate = null, string confirmText = "OK");

    /// <summary>Shows any dialog view model and returns the value passed to <see cref="DialogViewModel.Close"/>.</summary>
    Task<object?> ShowAsync(DialogViewModel dialog);
}

/// <summary>
/// Shows dialogs as modal windows centered over the current window on the desktop, and as an in-page
/// overlay in the browser (which has no windows).
/// </summary>
public sealed class DialogService : IDialogService
{
    public Task ShowMessageAsync(string title, string message, MessageKind kind = MessageKind.Info)
    {
        var vm = new MessageDialogViewModel(title, message, kind);
        vm.Buttons.Add(new DialogButton("OK", true, isDefault: true, isCancel: true));
        return ShowAsync(vm);
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", string cancelText = "Cancel", MessageKind kind = MessageKind.Question)
    {
        var vm = new MessageDialogViewModel(title, message, kind);
        vm.Buttons.Add(new DialogButton(confirmText, true, isDefault: true));
        vm.Buttons.Add(new DialogButton(cancelText, false, isCancel: true));
        return await ShowAsync(vm) is true;
    }

    public async Task<SaveChoice> AskSaveChangesAsync(string documentName)
    {
        var vm = new MessageDialogViewModel("Unsaved Changes", $"Do you want to save the changes made to '{documentName}'?", MessageKind.Warning);
        vm.Buttons.Add(new DialogButton("Save", SaveChoice.Save, isDefault: true));
        vm.Buttons.Add(new DialogButton("Don't Save", SaveChoice.DontSave));
        vm.Buttons.Add(new DialogButton("Cancel", SaveChoice.Cancel, isCancel: true));
        return await ShowAsync(vm) is SaveChoice choice ? choice : SaveChoice.Cancel;
    }

    public async Task<string?> PromptAsync(string title, string label, string initialText = "", Func<string, string?>? validate = null, string confirmText = "OK")
    {
        var vm = new MessageDialogViewModel(title, label, MessageKind.Info)
        {
            ShowInput = true,
            InputText = initialText,
            Validator = validate,
        };
        vm.Buttons.Add(new DialogButton(confirmText, true, isDefault: true) { RequiresValidInput = true });
        vm.Buttons.Add(new DialogButton("Cancel", false, isCancel: true));
        return await ShowAsync(vm) is true ? vm.InputText.Trim() : null;
    }

    public async Task<object?> ShowAsync(DialogViewModel dialog)
    {
        var topLevel = AppServices.Shell?.CurrentTopLevel;

        if (topLevel is Window owner)
        {
            var window = new DialogWindow(dialog);
            await window.ShowDialog(owner);
            return await dialog.Result;
        }

        var overlay = Views.MainView.Current;
        if (overlay != null)
        {
            overlay.PushDialog(dialog);
            try
            {
                return await dialog.Result;
            }
            finally
            {
                overlay.PopDialog(dialog);
            }
        }

        return null;
    }
}

/// <summary>Modal window used for dialogs on the desktop.</summary>
public sealed class DialogWindow : Window
{
    private readonly DialogViewModel _dialog;

    public DialogWindow(DialogViewModel dialog)
    {
        _dialog = dialog;
        Title = dialog.Title;
        Content = dialog;
        SizeToContent = dialog.Resizable ? SizeToContent.Manual : SizeToContent.WidthAndHeight;
        if (dialog.Resizable)
        {
            Width = dialog.PreferredWidth;
            Height = dialog.PreferredHeight;
        }
        CanResize = dialog.Resizable;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = ShellWindow.LoadIcon();
        MinWidth = 320;

        dialog.CloseRequested += Close;
        KeyDown += OnKeyDown;
        Controls.FocusOut.Attach(this);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _dialog.CanCancelWithEscape)
        {
            _dialog.Cancel();
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _dialog.CloseRequested -= Close;
        _dialog.Cancel();
    }
}
