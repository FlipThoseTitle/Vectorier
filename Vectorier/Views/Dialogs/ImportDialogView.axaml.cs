using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Vectorier.ViewModels.Dialogs;

namespace Vectorier.Views.Dialogs;

public partial class ImportDialogView : UserControl
{
    public ImportDialogView()
    {
        InitializeComponent();
        DragDrop.AddDragOverHandler(DropZone, (_, e) =>
            e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None);
        DragDrop.AddDropHandler(DropZone, (_, e) =>
        {
            if (DataContext is ImportDialogViewModel vm && e.DataTransfer.TryGetFiles() is { } files)
                vm.Add(files.ToList());
        });
    }
}
