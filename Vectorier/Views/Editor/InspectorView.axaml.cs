using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Vectorier.ViewModels.Editor;

namespace Vectorier.Views.Editor;

public partial class InspectorView : UserControl
{
    private InspectorViewModel? _vm;

    public InspectorView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null) _vm.TargetChanging -= CommitFocusedField;
        _vm = DataContext as InspectorViewModel;
        if (_vm != null) _vm.TargetChanging += CommitFocusedField;
    }

    /// <summary>A name being typed belongs to the element it was typed for, not to the next one selected.</summary>
    private void CommitFocusedField()
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox box && this.IsVisualAncestorOf(box))
            Avalonia.Data.BindingOperations.GetBindingExpressionBase(box, TextBox.TextProperty)?.UpdateSource();
    }

    private void OnOpenTriggerEditor(object? sender, RoutedEventArgs e)
    {
        if (DataContext is InspectorViewModel vm)
            vm.OpenTriggerEditorCommand.Execute(null);
    }

    private void OnSelectTrick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is InspectorViewModel vm)
            vm.SelectTrickCommand.Execute(null);
    }

    private void OnPreviewMove(object? sender, RoutedEventArgs e)
    {
        if (DataContext is InspectorViewModel vm)
            vm.PreviewMoveCommand.Execute(null);
    }

    private string? _moveBeforeDrop;

    private void OnSpawnMoveOpened(object? sender, System.EventArgs e)
        => _moveBeforeDrop = (sender as ComboBox)?.DataContext is Vectorier.Models.Scene.SpawnElement spawn ? spawn.MoveName : null;

    private void OnSpawnMoveClosed(object? sender, System.EventArgs e)
    {
        if (sender is ComboBox { DataContext: Vectorier.Models.Scene.SpawnElement spawn } && DataContext is InspectorViewModel vm
            && spawn.MoveName != _moveBeforeDrop)
            _ = vm.PickSpawnFrameAsync(spawn);
    }

    private void OnPickSpawnFrame(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: Vectorier.Models.Scene.SpawnElement spawn } && DataContext is InspectorViewModel vm)
            _ = vm.PickSpawnFrameAsync(spawn);
    }
}
