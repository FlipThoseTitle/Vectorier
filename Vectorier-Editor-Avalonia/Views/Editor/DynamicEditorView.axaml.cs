using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Vectorier_Editor_Avalonia.Services;
using Vectorier_Editor_Avalonia.ViewModels.Editor;

namespace Vectorier_Editor_Avalonia.Views.Editor;

public partial class DynamicEditorView : UserControl
{
    public DynamicEditorView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
    }

    private DynamicEditorViewModel? Vm => DataContext as DynamicEditorViewModel;

    private static DynamicTrack? TrackOf(object? sender) => (sender as Control)?.Tag as DynamicTrack;

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (Vm == null || e.Source is TextBox)
            return;
        bool ctrl = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        if (ctrl && e.Key == Key.C) { Vm.CopyKeys(); e.Handled = true; }
        else if (ctrl && e.Key == Key.V) { Vm.PasteKeys(); e.Handled = true; }
        else if (!ctrl && e.Key == Key.Space) { Vm.TogglePlay(); e.Handled = true; }
        else if (!ctrl && e.Key == Key.Delete) { Vm.DeleteKeyCommand.Execute(Vm.ActiveTrack); e.Handled = true; }
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
        string? name = await AppServices.Dialogs.PromptAsync("Rename Transformation", "Name:", track.TransformationName,
            text => string.IsNullOrWhiteSpace(text) ? "Enter a name." : null);
        if (name != null)
            Vm.RenameTransformation(track, name);
    }
}
