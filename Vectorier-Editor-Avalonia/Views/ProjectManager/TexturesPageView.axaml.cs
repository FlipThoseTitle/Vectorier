using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Vectorier_Editor_Avalonia.Controls;
using Vectorier_Editor_Avalonia.ViewModels.ProjectManager;

namespace Vectorier_Editor_Avalonia.Views.ProjectManager;

/// <summary>
/// Texture grid. Tiles are laid out in rows inside a virtualizing list, so only the rows on screen exist and
/// only their textures are loaded (sprite sheets animate while visible).
/// </summary>
public partial class TexturesPageView : UserControl
{
    private const double TileWidth = 116; // tile + margins
    private TexturesPageViewModel? _vm;
    private bool _scheduled;
    private int _perRow;

    public TexturesPageView()
    {
        InitializeComponent();
        Rows.SelectionChanged += (_, _) =>
        {
            if (Rows.SelectedIndex >= 0) Rows.SelectedIndex = -1; // rows are not selectable; tiles are
        };
        Rows.AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
        GridArea.SizeChanged += (_, _) =>
        {
            if (PerRow() != _perRow) Schedule();
        };
        DragDrop.AddDragOverHandler(DropArea, (_, e) => e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None);
        DragDrop.AddDropHandler(DropArea, async (_, e) =>
        {
            var files = e.DataTransfer.TryGetFiles();
            if (files != null && DataContext is TexturesPageViewModel vm)
                await vm.ImportItemsAsync(files);
        });
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null) _vm.ItemsRebuilt -= Schedule;
        _vm = DataContext as TexturesPageViewModel;
        if (_vm != null) _vm.ItemsRebuilt += Schedule;
        Schedule();
    }

    private int PerRow() => Math.Max(1, (int)((GridArea.Bounds.Width - 18) / TileWidth));

    private void Schedule()
    {
        if (_scheduled) return;
        _scheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _scheduled = false;
            _perRow = PerRow();
            Rows.ItemsSource = _vm == null ? null : ItemRow.Chunk(_vm.Items, _perRow);
        }, DispatcherPriority.Background);
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null || !e.GetCurrentPoint(Rows).Properties.IsLeftButtonPressed)
            return;
        var visual = e.Source as Visual;
        while (visual != null && visual is not Control { Tag: TextureItemViewModel })
            visual = visual.GetVisualParent();
        if (visual is not Control { Tag: TextureItemViewModel item })
        {
            if (e.KeyModifiers == KeyModifiers.None) _vm.ClearSelection();
            return;
        }
        var mods = e.KeyModifiers;
        bool ctrl = (mods & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        bool shift = (mods & KeyModifiers.Shift) != 0;
        _vm.Click(item, ctrl, shift);
        e.Handled = true;
    }
}
