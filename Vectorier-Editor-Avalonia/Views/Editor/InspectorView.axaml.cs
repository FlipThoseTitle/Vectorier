using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Vectorier_Editor_Avalonia.ViewModels.Editor;

namespace Vectorier_Editor_Avalonia.Views.Editor;

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
}
