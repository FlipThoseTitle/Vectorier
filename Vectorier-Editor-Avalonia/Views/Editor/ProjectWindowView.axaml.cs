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
        FolderList.AddHandler(PointerReleasedEvent, OnFolderReleased, RoutingStrategies.Tunnel);
        FolderScroll.AddHandler(PointerPressedEvent, OnFolderAreaPressed, RoutingStrategies.Bubble, handledEventsToo: false);
        _ = new InternalDrag(TileRows, DragPayloadForTiles);
        _ = new InternalDrag(FolderList, () => _pressedFolder != null && _vm?.CanReorder(_pressedFolder) == true ? _pressedFolder : null);

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

    private static PwSelect ModeFrom(KeyModifiers mods) =>
        (mods & KeyModifiers.Shift) != 0 ? PwSelect.Range :
        (mods & (KeyModifiers.Control | KeyModifiers.Meta)) != 0 ? PwSelect.Toggle : PwSelect.Replace;

    /// <summary>One tile drags itself (to the Scene, the Hierarchy or a folder); several selected tiles go to a folder only.</summary>
    private object? DragPayloadForTiles()
    {
        if (_vm == null || _pressedItem == null)
            return null;
        if (_vm.HasMultipleItems && _vm.SelectedItems.Contains(_pressedItem))
            return _vm.ShowsFolders && !_vm.IsObjectContext ? new PwItemGroup(_vm.SelectedItems.ToList()) : null;
        return _pressedItem is { CanPlace: true } || _pressedItem is { Texture: not null } ? _pressedItem : null;
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
        _pressPoint = e.GetPosition(this);
        _vm.LastClickInFolders = false;
        Focus();
        var point = e.GetCurrentPoint(TileRows);
        if (item == null)
        {
            if (point.Properties.IsLeftButtonPressed)
                _vm.SelectedItem = null;
            else if (point.Properties.IsRightButtonPressed)
                ShowContentMenu();
            return;
        }
        if (point.Properties.IsRightButtonPressed)
        {
            // Right click keeps a multiple selection that contains the tile.
            if (!_vm.SelectedItems.Contains(item))
                _vm.SelectItem(item);
            ShowItemMenu(item);
            return;
        }
        var mode = ModeFrom(e.KeyModifiers);
        // Pressing a tile of a multiple selection keeps it (so it can be dragged); a click without drag selects it alone.
        if (mode == PwSelect.Replace && _vm.HasMultipleItems && _vm.SelectedItems.Contains(item) && e.ClickCount == 1)
        {
            _clickReplaces = item;
            return;
        }
        _vm.SelectItem(item, mode);
        if (mode == PwSelect.Replace && e.ClickCount == 2 && item.Definition != null && item.PlatformSurface == null)
            _ = _vm.EditObjectAsync(item); // double click: Edit Object
    }

    private PwItem? _clickReplaces;
    private Point _pressPoint;

    private bool IsClick(PointerEventArgs e)
    {
        var d = e.GetPosition(this) - _pressPoint;
        return Math.Abs(d.X) < 4 && Math.Abs(d.Y) < 4;
    }

    private void OnTileReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_clickReplaces != null && _vm != null && IsClick(e))
            _vm.SelectItem(_clickReplaces);
        _clickReplaces = null;
        _pressedItem = null;
    }

    /// <summary>Right click on the empty part of the grid.</summary>
    private void ShowContentMenu()
    {
        if (_vm == null || !_vm.CanImport)
            return;
        var menu = new ContextMenu();
        var import = new MenuItem { Header = _vm.ImportLabel, Icon = new PathIcon { Data = (Avalonia.Media.Geometry)this.FindResource("Icon.Import")!, Width = 13, Height = 13 } };
        import.Click += (_, _) => _vm.ImportCommand.Execute(null);
        menu.Items.Add(import);
        menu.Open(TileRows);
    }

    private void ShowItemMenu(PwItem item)
    {
        if (_vm == null)
            return;
        var menu = new ContextMenu();
        bool multiple = _vm.HasMultipleItems;
        if (!multiple && item.Definition != null && item.PlatformSurface == null)
        {
            var edit = new MenuItem { Header = "Edit Object" };
            edit.Click += async (_, _) => await _vm.EditObjectAsync(item);
            menu.Items.Add(edit);
            var renameObject = new MenuItem { Header = "Rename..." };
            renameObject.Click += async (_, _) => await _vm.RenameDefinitionAsync(item);
            menu.Items.Add(renameObject);
        }
        bool canRename = !multiple && (item.Texture != null || item.Trigger != null || item.Area != null);
        bool canDelete = _vm.CanDelete(item);
        if (canRename)
        {
            var rename = new MenuItem { Header = "Rename..." };
            rename.Click += (_, _) => _vm.RenameItemCommand.Execute(item);
            menu.Items.Add(rename);
        }
        if (canDelete)
        {
            var delete = new MenuItem { Header = multiple ? $"Delete {_vm.SelectedItems.Count} Items" : "Delete", InputGesture = new KeyGesture(Key.Delete) };
            delete.Click += (_, _) => _vm.DeleteItemCommand.Execute(item);
            menu.Items.Add(delete);
        }
        if (_vm.ShowsFolders && _vm.Context != PwContext.Objects)
        {
            var move = new MenuItem { Header = "Move to Folder" };
            var moving = multiple ? _vm.SelectedItems.ToList() : new List<PwItem> { item };
            foreach (var folder in _vm.AllFolders)
            {
                var target = new MenuItem { Header = folder.IsUnorganized ? "Unorganized" : folder.Name };
                var f = folder;
                target.Click += async (_, _) => await _vm.AssignItemsAsync(moving, f);
                move.Items.Add(target);
            }
            menu.Items.Add(move);
        }
        if (_vm.CanImport)
        {
            menu.Items.Add(new Separator());
            var import = new MenuItem { Header = _vm.ImportLabel };
            import.Click += (_, _) => _vm.ImportCommand.Execute(null);
            menu.Items.Add(import);
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
        _pressPoint = e.GetPosition(this);
        _vm.LastClickInFolders = true;
        Focus();
        if (folder == null)
            return;
        if (e.GetCurrentPoint(FolderList).Properties.IsRightButtonPressed)
        {
            if (!_vm.SelectedFolders.Contains(folder))
                _vm.SelectFolder(folder);
            ShowFolderMenu(folder);
            e.Handled = true;
            return;
        }
        var mode = ModeFrom(e.KeyModifiers);
        if (mode == PwSelect.Replace && _vm.HasMultipleFolders && _vm.SelectedFolders.Contains(folder))
        {
            // Keep the multiple selection for a drag; a plain click selects the folder alone on release.
            _folderClickReplaces = folder;
            return;
        }
        _vm.SelectFolder(folder, mode);
    }

    private PwFolder? _folderClickReplaces;

    private void OnFolderReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_folderClickReplaces != null && _vm != null && IsClick(e))
            _vm.SelectFolder(_folderClickReplaces);
        _folderClickReplaces = null;
    }

    private void OnFolderAreaPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm != null)
            _vm.LastClickInFolders = true;
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
        bool multiple = folder != null && _vm.HasMultipleFolders && _vm.SelectedFolders.Contains(folder);
        if (folder is { IsUserFolder: true })
        {
            if (!multiple)
            {
                var rename = new MenuItem { Header = "Rename..." };
                rename.Click += (_, _) => _vm.RenameFolderCommand.Execute(folder);
                menu.Items.Add(rename);
            }
            var delete = new MenuItem { Header = multiple ? $"Delete {_vm.SelectedFolders.Count} Folders" : "Delete Folder", InputGesture = new KeyGesture(Key.Delete) };
            delete.Click += (_, _) => _vm.DeleteFolderCommand.Execute(folder);
            menu.Items.Add(delete);
        }
        if (_vm.IsObjectContext && folder is { IsFile: true })
        {
            var delete = new MenuItem { Header = multiple ? $"Delete {_vm.SelectedFolders.Count} Files..." : "Delete File...", InputGesture = new KeyGesture(Key.Delete) };
            delete.Click += (_, _) => _vm.DeleteFolderCommand.Execute(folder);
            menu.Items.Add(delete);
        }
        if (_vm.IsObjectContext && folder is { IsFile: true } file && !multiple)
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

    /// <summary>Insertion point for a dragged folder: index in AllFolders and the line's Y in the folder panel.</summary>
    private (int Index, double Y)? FolderInsertAt(Point position)
    {
        if (_vm == null)
            return null;
        var p = this.TranslatePoint(position, FolderScroll);
        if (p == null || !new Rect(FolderScroll.Bounds.Size).Contains(p.Value))
            return null;
        Border? last = null;
        foreach (var border in FolderList.GetVisualDescendants().OfType<Border>().Where(b => b.Tag is PwFolder && b.Classes.Contains("folder")))
        {
            var top = border.TranslatePoint(new Point(0, 0), FolderScroll);
            if (top == null)
                continue;
            var folder = (PwFolder)border.Tag!;
            if (p.Value.Y < top.Value.Y + border.Bounds.Height / 2)
                return (_vm.AllFolders.ToList().IndexOf(folder), top.Value.Y);
            last = border;
        }
        if (last?.Tag is PwFolder lastFolder && last.TranslatePoint(new Point(0, last.Bounds.Height), FolderScroll) is { } bottom)
            return (_vm.AllFolders.ToList().IndexOf(lastFolder) + 1, bottom.Y);
        return null;
    }

    public bool DragOver(object payload, Point position)
    {
        ClearDrop();
        if (_vm == null)
            return false;
        switch (payload)
        {
            case PwItem or PwItemGroup when _vm.ShowsFolders && !_vm.IsObjectContext:
            {
                // Into a folder: the folder is highlighted.
                var border = FolderBorderAt(position);
                if (border == null || border.Tag is not PwFolder { IsFile: false })
                    return false;
                border.Classes.Add("drop");
                _dropFolder = border;
                return true;
            }
            case PwFolder folder when _vm.CanReorder(folder):
            {
                // Reordering: a line shows where the folders go (nothing is put inside another folder).
                if (FolderInsertAt(position) is not { } insert)
                    return false;
                double y = Math.Clamp(insert.Y - 1, 0, Math.Max(0, FolderScroll.Bounds.Height - 2));
                FolderDropLine.Margin = new Thickness(2, FolderScroll.Bounds.Y + y, 2, 0);
                FolderDropLine.IsVisible = true;
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
        FolderDropLine.IsVisible = false;
    }

    public async void Drop(object payload, Point position, KeyModifiers modifiers)
    {
        ClearDrop();
        if (_vm == null)
            return;
        switch (payload)
        {
            case PwItem item when FolderBorderAt(position) is { Tag: PwFolder { IsFile: false } folder }:
                await _vm.AssignItemsAsync(new[] { item }, folder);
                break;
            case PwItemGroup group when FolderBorderAt(position) is { Tag: PwFolder { IsFile: false } folder }:
                await _vm.AssignItemsAsync(group.Items, folder);
                break;
            case PwFolder source when FolderInsertAt(position) is { } insert:
            {
                var moving = _vm.SelectedFolders.Contains(source) ? _vm.SelectedFolders.ToList() : new List<PwFolder> { source };
                await _vm.MoveFoldersAsync(moving, insert.Index);
                break;
            }
            case HierarchyDragPayload drag when OverContent(position):
                await _vm.AcceptElementsAsync(drag.Elements);
                break;
        }
    }
}
