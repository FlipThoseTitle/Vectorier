using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Vectorier.Services;
using Vectorier.Services.Localization;
using Vectorier.ViewModels.Editor;

namespace Vectorier.Views.Editor;

public partial class DynamicEditorView : UserControl
{
    public DynamicEditorView()
    {
        InitializeComponent();
    }

    private DynamicEditorViewModel? Vm => DataContext as DynamicEditorViewModel;

    private static DynamicTrack? TrackOf(object? sender) => (sender as Control)?.Tag as DynamicTrack;

    /// <summary>
    /// Keyframe shortcuts while the focus is in the Dynamic Editor (the timeline takes it when clicked). Called by the
    /// editor's shortcut handler before the Scene's Copy / Paste / Delete... Undo and Redo stay the editor's (they
    /// include the timelines). Returns true when handled.
    /// </summary>
    public bool HandleKey(Key key, KeyModifiers mods)
    {
        if (Vm is not { IsBound: true } vm)
            return false;
        bool ctrl = (mods & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        bool shift = (mods & KeyModifiers.Shift) != 0;
        if (ctrl && !shift)
        {
            switch (key)
            {
                case Key.C: vm.CopyKeys(); return true;
                case Key.X: vm.CutKeys(); return true;
                case Key.V: vm.PasteKeys(); return true;
                case Key.D: vm.DuplicateKeys(); return true;
                case Key.A: vm.SelectAllKeys(); return true;
            }
            return false;
        }
        if (ctrl)
            return false;
        switch (key)
        {
            case Key.Space: vm.TogglePlay(); return true;
            case Key.Delete or Key.Back: vm.DeleteKeyCommand.Execute(vm.ActiveTrack); return true;
            case Key.Left: vm.StepCommand.Execute(shift ? "-10" : "-1"); return true;
            case Key.Right: vm.StepCommand.Execute(shift ? "10" : "1"); return true;
        }
        return false;
    }

    private void OnAddKey(object? sender, RoutedEventArgs e) => Vm?.AddKeyCommand.Execute(TrackOf(sender));
    private void OnDeleteKey(object? sender, RoutedEventArgs e) => Vm?.DeleteKeyCommand.Execute(TrackOf(sender));
    private void OnClear(object? sender, RoutedEventArgs e) => Vm?.ClearKeysCommand.Execute(TrackOf(sender));
    private void OnSave(object? sender, RoutedEventArgs e) => Vm?.SaveTransformationCommand.Execute(TrackOf(sender));
    private void OnNewTransformation(object? sender, RoutedEventArgs e) => Vm?.NewTransformationCommand.Execute(TrackOf(sender));
    private void OnRemoveTransformation(object? sender, RoutedEventArgs e) => Vm?.RemoveTransformationCommand.Execute(TrackOf(sender));

    private void OnEndChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (Vm != null && TrackOf(sender) is { } track && e.NewValue is decimal value && (int)value != track.TotalFrames)
            Vm.SetTotalFrames(track, (int)value);
    }

    private void OnTransformationPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (Vm != null && TrackOf(sender) is { } track && (sender as ComboBox)?.SelectedItem is string name && name != track.TransformationName)
            Vm.SelectTransformation(track, name);
    }

    private async void OnRename(object? sender, RoutedEventArgs e)
    {
        if (Vm == null || TrackOf(sender) is not { } track)
            return;
        string? name = await AppServices.Dialogs.PromptAsync(Loc.T("Rename Transformation"), Loc.T("Name:"), track.TransformationName,
            text => string.IsNullOrWhiteSpace(text) ? Loc.T("Enter a name.") : null);
        if (name != null)
            Vm.RenameTransformation(track, name);
    }
}
