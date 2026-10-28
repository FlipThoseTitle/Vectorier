using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Vectorier_Editor_Avalonia.Controls;
using Vectorier_Editor_Avalonia.Services.Assets;
using Vectorier_Editor_Avalonia.ViewModels.Editor;

namespace Vectorier_Editor_Avalonia.Views.Editor;

/// <summary>One line of tiles (the grid is split into rows so only visible rows are created).</summary>
public sealed class TileRow
{
    public TileRow(IReadOnlyList<PwItem> items) => Items = items;
    public IReadOnlyList<PwItem> Items { get; }
}

public partial class ProjectWindowView : UserControl, IInternalDropTarget
{
    public static readonly StyledProperty<double> TileWidthProperty =
        AvaloniaProperty.Register<ProjectWindowView, double>(nameof(TileWidth), 104);

    public static readonly StyledProperty<double> ThumbHeightProperty =
        AvaloniaProperty.Register<ProjectWindowView, double>(nameof(ThumbHeight), 80);

    public static readonly StyledProperty<TextureLibrary?> LibraryProperty =
        AvaloniaProperty.Register<ProjectWindowView, TextureLibrary?>(nameof(Library));

    private ProjectWindowViewModel? _vm;
    private bool _rowsScheduled;
    private Border? _dropFolder;
    private PwItem? _pressedItem;
    private PwFolder? _pressedFolder;

    public ProjectWindowView()
    {
        InitializeComponent();

        TileRows.AddHandler(PointerPressedEvent, OnTilePressed, RoutingStrategies.Tunnel);
        TileRows.AddHandler(PointerReleasedEvent, OnTileReleased, RoutingStrategies.Tunnel);
        FolderList.AddHandler(PointerPressedEvent, OnFolderPressed, RoutingStrategies.Tunnel);
        FolderScroll.AddHandler(PointerPressedEvent, OnFolderAreaPressed, RoutingStrategies.Bubble, handledEventsToo: false);
        _ = new InternalDrag(TileRows, () => _pressedItem is { CanPlace: true } || _pressedItem is { Texture: not null } ? _pressedItem : null);
        _ = new InternalDrag(FolderList, () => _pressedFolder);

        ContentArea.SizeChanged += (_, _) => ScheduleRows();
        // The ListBox only virtualizes rows; tiles draw their own selection.
        TileRows.SelectionChanged += (_, _) =>
        {
            if (TileRows.SelectedIndex >= 0)
                TileRows.SelectedIndex = -1;
        };
        DragDrop.AddDragOverHandler(DropArea, (_, e) =>
            e.DragEffects = _vm?.AcceptsFileDrop == true && e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None);
        DragDrop.AddDropHandler(DropArea, async (_, e) =>
        {
            var files = e.DataTransfer.TryGetFiles();
            if (files != null && _vm != null)
                await _vm.ImportFilesAsync(files.ToList());
        });
    }

    public double TileWidth
    {
        get => GetValue(TileWidthProperty);
        set => SetValue(TileWidthProperty, value);
    }

    public double ThumbHeight
    {
        get => GetValue(ThumbHeightProperty);
        set => SetValue(ThumbHeightProperty, value);
    }

    public TextureLibrary? Library
    {
        get => GetValue(LibraryProperty);
        set => SetValue(LibraryProperty, value);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null)
        {
            _vm.Items.CollectionChanged -= OnItemsChanged;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }
        _vm = DataContext as ProjectWindowViewModel;
        if (_vm != null)
        {
            _vm.Items.CollectionChanged += OnItemsChanged;
            _vm.PropertyChanged += OnVmPropertyChanged;
            Library = _vm.Textures;
            UpdateTileSize();
        }
        ScheduleRows();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectWindowViewModel.TileSize))
        {
            UpdateTileSize();
            ScheduleRows();
        }
        else if (e.PropertyName is nameof(ProjectWindowViewModel.ShowsItems) or nameof(ProjectWindowViewModel.Context))
        {
            ScheduleRows();
        }
    }

    private void UpdateTileSize()
    {
        double size = _vm?.TileSize ?? 96;
        TileWidth = size + 8;
        ThumbHeight = size * 0.8;
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScheduleRows();

    private void ScheduleRows()
    {
        if (_rowsScheduled)
            return;
        _rowsScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _rowsScheduled = false;
            BuildRows();
        }, DispatcherPriority.Background);
    }

    private void BuildRows()
    {
        if (_vm == null)
        {
            TileRows.ItemsSource = null;
            return;
        }
        double available = Math.Max(1, ContentArea.Bounds.Width - 18);
        int perRow = Math.Max(1, (int)(available / (TileWidth + 6)));
        var rows = new List<TileRow>();
        var items = _vm.Items.ToList();
        for (int i = 0; i < items.Count; i += perRow)
            rows.Add(new TileRow(items.Skip(i).Take(perRow).ToList()));
        TileRows.ItemsSource = rows;
        EmptyHint.IsVisible = items.Count == 0;
    }

    private static T? TagFrom<T>(object? source) where T : class
    {
        var visual = source as Visual;
        while (visual != null)
        {
            if (visual is Control { Tag: T tag })
                return tag;
            visual = visual.GetVisualParent();
        }
        return null;
    }

    // ================= TILES ================= //

    private void OnTileAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // Model previews load only when their tile becomes visible (rows are virtualized).
        if (sender is Control { Tag: PwItem { ModelName: not null } item })
            _vm?.RequestModelPreview(item);
    }

    private void OnTilePressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null)
            return;
        var item = TagFrom<PwItem>(e.Source);
        _pressedItem = item;
        var point = e.GetCurrentPoint(TileRows);
        if (item == null)
        {
            if (point.Properties.IsLeftButtonPressed)
                _vm.SelectedItem = null;
            return;
        }
        _vm.SelectedItem = item;
        if (point.Properties.IsRightButtonPressed)
            ShowItemMenu(item);
        else if (e.ClickCount == 2 && item.Definition != null && item.PlatformSurface == null)
            _ = _vm.EditObjectAsync(item); // double click: Edit Object
    }

    private void OnTileReleased(object? sender, PointerReleasedEventArgs e) => _pressedItem = null;

    private void ShowItemMenu(PwItem item)
    {
        if (_vm == null)
            return;
        var menu = new ContextMenu();
        if (item.Definition != null && item.PlatformSurface == null)
        {
            var edit = new MenuItem { Header = "Edit Object" };
            edit.Click += async (_, _) => await _vm.EditObjectAsync(item);
            menu.Items.Add(edit);
            var renameObject = new MenuItem { Header = "Rename..." };
            renameObject.Click += async (_, _) => await _vm.RenameDefinitionAsync(item);
            menu.Items.Add(renameObject);
        }
        bool canRename = item.Texture != null || item.Trigger != null || item.Area != null;
        bool canDelete = _vm.CanDelete(item);
        if (canRename)
        {
            var rename = new MenuItem { Header = "Rename..." };
            rename.Click += (_, _) => _vm.RenameItemCommand.Execute(item);
            menu.Items.Add(rename);
        }
        if (canDelete)
        {
            var delete = new MenuItem { Header = "Delete" };
            delete.Click += (_, _) => _vm.DeleteItemCommand.Execute(item);
            menu.Items.Add(delete);
        }
        if (_vm.ShowsFolders && _vm.Context != PwContext.Objects)
        {
            var move = new MenuItem { Header = "Move to Folder" };
            foreach (var folder in _vm.Folders)
            {
                var target = new MenuItem { Header = folder.IsUnorganized ? "Unorganized" : folder.Name };
                var f = folder;
                target.Click += async (_, _) => await _vm.AssignItemsAsync(new[] { item }, f);
                move.Items.Add(target);
            }
            menu.Items.Add(move);
        }
        if (menu.Items.Count > 0)
            menu.Open(TileRows);
    }

    // ================= FOLDERS ================= //

    private void OnFolderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null)
            return;
        var folder = TagFrom<PwFolder>(e.Source);
        _pressedFolder = folder;
        if (folder == null)
            return;
        _vm.SelectedFolder = folder;
        if (e.GetCurrentPoint(FolderList).Properties.IsRightButtonPressed)
        {
            ShowFolderMenu(folder);
            e.Handled = true;
        }
    }

    private void OnFolderAreaPressed(object? sender, PointerPressedEventArgs e)
    {
        // Right click on the empty part of the folder list: create a folder.
        if (_vm == null || TagFrom<PwFolder>(e.Source) != null)
            return;
        if (e.GetCurrentPoint(FolderScroll).Properties.IsRightButtonPressed)
            ShowFolderMenu(null);
    }

    private void ShowFolderMenu(PwFolder? folder)
    {
        if (_vm == null)
            return;
        var menu = new ContextMenu();
        var create = new MenuItem { Header = _vm.IsObjectContext ? "New Objects File..." : "New Folder..." };
        create.Click += (_, _) => _vm.CreateFolderCommand.Execute(null);
        menu.Items.Add(create);
        if (folder is { IsUserFolder: true })
        {
            var rename = new MenuItem { Header = "Rename..." };
            rename.Click += (_, _) => _vm.RenameFolderCommand.Execute(folder);
            var delete = new MenuItem { Header = "Delete Folder" };
            delete.Click += (_, _) => _vm.DeleteFolderCommand.Execute(folder);
            menu.Items.Add(rename);
            menu.Items.Add(delete);
        }
        if (_vm.IsObjectContext && folder is { IsFile: true } file)
        {
            // Object XML type: Object (X / Y) or Building (In / Out / Box attributes).
            menu.Items.Add(new Separator());
            var type = new MenuItem { Header = "Type" };
            foreach (bool building in new[] { false, true })
            {
                var option = new MenuItem
                {
                    Header = building ? "Building" : "Object",
                    ToggleType = MenuItemToggleType.Radio,
                    IsChecked = (file.IsBuilding == true) == building,
                };
                option.Click += async (_, _) => await _vm.SetFileTypeAsync(file, building);
                type.Items.Add(option);
            }
            menu.Items.Add(type);
        }
        menu.Open(FolderList);
    }

    // ================= INTERNAL DROPS ================= //

    private Border? FolderBorderAt(Point position)
    {
        var p = this.TranslatePoint(position, FolderList);
        if (p == null || !new Rect(FolderList.Bounds.Size).Contains(p.Value))
            return null;
        var hit = FolderList.InputHitTest(p.Value) as Visual;
        while (hit != null && hit is not Border { Tag: PwFolder })
            hit = hit.GetVisualParent();
        return hit as Border;
    }

    private bool OverContent(Point position)
    {
        var p = this.TranslatePoint(position, ContentArea);
        return p != null && new Rect(ContentArea.Bounds.Size).Contains(p.Value);
    }

    public bool DragOver(object payload, Point position)
    {
        ClearDrop();
        if (_vm == null)
            return false;
        switch (payload)
        {
            case PwItem when _vm.ShowsFolders && !_vm.IsObjectContext:
            case PwFolder:
            {
                var border = FolderBorderAt(position);
                if (border == null)
                    return false;
                border.Classes.Add("drop");
                _dropFolder = border;
                return true;
            }
            case HierarchyDragPayload drag:
                return OverContent(position) && _vm.CanAcceptElements(drag.Elements);
        }
        return false;
    }

    public void DragLeave() => ClearDrop();

    private void ClearDrop()
    {
        _dropFolder?.Classes.Remove("drop");
        _dropFolder = null;
    }

    public async void Drop(object payload, Point position, KeyModifiers modifiers)
    {
        ClearDrop();
        if (_vm == null)
            return;
        switch (payload)
        {
            case PwItem item when FolderBorderAt(position) is { Tag: PwFolder folder }:
                await _vm.AssignItemsAsync(new[] { item }, folder);
                break;
            case PwFolder source when FolderBorderAt(position) is { Tag: PwFolder target }:
                await _vm.MoveFolderAsync(source, target);
                break;
            case HierarchyDragPayload drag when OverContent(position):
                await _vm.AcceptElementsAsync(drag.Elements);
                break;
        }
    }
}
