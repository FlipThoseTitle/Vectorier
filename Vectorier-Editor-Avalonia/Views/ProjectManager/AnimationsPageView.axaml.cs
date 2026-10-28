using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Vectorier_Editor_Avalonia.ViewModels.ProjectManager;

namespace Vectorier_Editor_Avalonia.Views.ProjectManager;

public partial class AnimationsPageView : UserControl
{
    public AnimationsPageView()
    {
        InitializeComponent();
        List.SelectionChanged += (_, _) =>
        {
            if (DataContext is not AnimationsPageViewModel vm) return;
            vm.SelectedItems.Clear();
            foreach (var item in List.SelectedItems?.OfType<string>() ?? Enumerable.Empty<string>())
                vm.SelectedItems.Add(item);
        };
        DragDrop.AddDragOverHandler(DropArea, (_, e) => e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None);
        DragDrop.AddDropHandler(DropArea, async (_, e) =>
        {
            var files = e.DataTransfer.TryGetFiles();
            if (files != null && DataContext is AnimationsPageViewModel vm)
                await vm.ImportItemsAsync(files);
        });
    }

    private void OnItemDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is AnimationsPageViewModel vm && vm.CanPreview)
            vm.PreviewCommand.Execute(null);
    }
}
