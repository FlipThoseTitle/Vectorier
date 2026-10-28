using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Vectorier_Editor_Avalonia.ViewModels.ProjectManager;

namespace Vectorier_Editor_Avalonia.Views.ProjectManager;

public partial class FileListPageView : UserControl
{
    public FileListPageView()
    {
        InitializeComponent();
        List.SelectionChanged += (_, _) =>
        {
            if (DataContext is not FileListPageViewModel vm) return;
            vm.SelectedItems.Clear();
            foreach (var item in List.SelectedItems?.OfType<FileItemViewModel>() ?? Enumerable.Empty<FileItemViewModel>())
                vm.SelectedItems.Add(item);
        };
        DragDrop.AddDragOverHandler(DropArea, (_, e) => e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None);
        DragDrop.AddDropHandler(DropArea, async (_, e) =>
        {
            var files = e.DataTransfer.TryGetFiles();
            if (files != null && DataContext is FileListPageViewModel vm)
                await vm.ImportItemsAsync(files);
        });
    }
}
