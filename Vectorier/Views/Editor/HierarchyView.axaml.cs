using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Vectorier.Controls;
using Vectorier.Models.Scene;
using Vectorier.ViewModels.Editor;

namespace Vectorier.Views.Editor;

/// <summary>Elements dragged from the Hierarchy (to reorder them or to save them in the Project Window).</summary>
public sealed record HierarchyDragPayload(IReadOnlyList<SceneElement> Elements);

public partial class HierarchyView : UserControl, IInternalDropTarget
{
    private EditorViewModel? _vm;
    private Border? _dropRow;

    public HierarchyView()
    {
        InitializeComponent();
        RowList.AddHandler(PointerPressedEvent, OnRowPressed, RoutingStrategies.Tunnel);
        RowList.AddHandler(PointerReleasedEvent, OnRowReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        RowList.DoubleTapped += OnRowDoubleTapped;
        RowList.AddHandler(PointerMovedEvent, OnMarqueeMoved, RoutingStrategies.Tunnel);
        RowList.AddHandler(PointerReleasedEvent, OnMarqueeReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        RowList.AddHandler(PointerCaptureLostEvent, (_, _) => { EndMarquee(); _eyePaint = null; }, RoutingStrategies.Bubble);
        _ = new InternalDrag(RowList, CreatePayload);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null)
            _vm.Hierarchy.ScrollRequested -= OnScrollRequested;
        _vm = DataContext as EditorViewModel;
        if (_vm != null)
            _vm.Hierarchy.ScrollRequested += OnScrollRequested;
    }

    private void OnScrollRequested(int index)
    {
        if (index >= 0 && index < RowList.ItemCount)
            RowList.ScrollIntoView(index);
    }

    private static HierarchyRow? RowFrom(object? source)
    {
        var visual = source as Visual;
        while (visual != null)
        {
            if (visual is Border { Tag: HierarchyRow row })
                return row;
            visual = visual.GetVisualParent();
        }
        return null;
    }

    private static bool IsEye(Visual? visual)
    {
        for (var v = visual; v != null; v = v.GetVisualParent())
        {
            if (v is Border b && b.Classes.Contains("row-eye"))
                return true;
            if (v is Border { Tag: HierarchyRow })
                return false;
        }
        return false;
    }

    private void OnToggleAllVisibility(object? sender, RoutedEventArgs e)
    {
        if (_vm == null)
            return;
        var all = _vm.Document.AllElements().ToList();
        bool anyHidden = all.Any(x => x.IsHidden);
        if (anyHidden)
            foreach (var x in all) x.IsHidden = false;
        else
            foreach (var root in _vm.Document.Roots) root.IsHidden = true;
        UpdateAllEye();
    }

    private void UpdateAllEye()
    {
        if (_vm == null || Application.Current == null)
            return;
        bool anyHidden = _vm.Document.AllElements().Any(x => x.IsHidden);
        if (Application.Current.TryGetResource(anyHidden ? "Icon.EyeOff" : "Icon.Eye", null, out var res) && res is Avalonia.Media.Geometry g)
            AllEyeIcon.Data = g;
    }

    private HierarchyRow? _pressedRow;
    private bool _pressedWasSelected;

    private void OnRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null)
            return;
        // The expander handles its own click (Ctrl: with every descendant).
        if ((e.Source as Visual)?.FindAncestorOfType<Avalonia.Controls.Primitives.ToggleButton>(true) != null)
        {
            _expanderCtrl = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
            return;
        }

        var point = e.GetCurrentPoint(RowList);
        var row = RowFrom(e.Source);

        // The scrollbar keeps its own input (no rectangle selection, no context menu).
        if ((e.Source as Visual)?.FindAncestorOfType<Avalonia.Controls.Primitives.ScrollBar>(true) != null)
            return;

        // Eye: toggles the element's visibility without selecting it; dragging paints the same state across rows.
        if (row?.Element is { } eyeElement && point.Properties.IsLeftButtonPressed && IsEye(e.Source as Visual))
        {
            _pressedRow = null;
            _eyePaint = !eyeElement.IsHidden;
            eyeElement.IsHidden = _eyePaint.Value;
            UpdateAllEye();
            e.Pointer.Capture(RowList);
            e.Handled = true;
            return;
        }
        bool right = point.Properties.IsRightButtonPressed;
        bool ctrl = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;

        if (row == null)
        {
            _pressedRow = null;
            if (!right)
            {
                // Empty space: rectangle selection (any drag direction).
                if (!ctrl && !shift)
                    _vm.ClearSelection();
                _marqueeStart = point.Position;
                _marqueeAdd = ctrl || shift;
                _marqueeBase = _marqueeAdd ? _vm.Selection.ToList() : new List<SceneElement>();
                _marquee = true;
                e.Pointer.Capture(RowList);
                e.Handled = true;
            }
            else
            {
                ShowContextMenu(null);
            }
            return;
        }

        _pressedRow = row;
        _pressedWasSelected = row.Element?.IsSelected == true;

        // A plain click on an already selected row keeps the multi-selection until release (so it can be dragged).
        if (!right && !ctrl && !shift && _pressedWasSelected && _vm.Selection.Count > 1)
            return;

        _vm.Hierarchy.Click(row, ctrl, shift, right);
        if (right)
            ShowContextMenu(row.Element);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        // The ListBox only virtualizes; selection is drawn by the rows themselves.
        RowList.SelectionChanged += (_, _) =>
        {
            if (RowList.SelectedIndex >= 0)
                RowList.SelectedIndex = -1;
        };
    }

    private void OnRowReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_vm == null || _pressedRow == null || InternalDrag.IsDragging)
            return;
        if (e.InitialPressMouseButton == MouseButton.Left && _pressedWasSelected && _vm.Selection.Count > 1
            && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Meta)) == 0
            && RowFrom(e.Source) == _pressedRow)
        {
            _vm.Hierarchy.Click(_pressedRow, false, false, false);
        }
        _pressedRow = null;
    }

    // ================= RECTANGLE SELECTION ================= //

    private const double RowHeight = 20;
    private bool _marquee;
    private bool _marqueeAdd;
    private Point _marqueeStart;
    private List<SceneElement> _marqueeBase = new();

    private bool? _eyePaint;

    private void OnMarqueeMoved(object? sender, PointerEventArgs e)
    {
        if (_eyePaint is { } paint)
        {
            var hit = RowList.InputHitTest(e.GetPosition(RowList)) as Visual;
            if (IsEye(hit) && RowFrom(hit)?.Element is { } element && element.IsHidden != paint)
            {
                element.IsHidden = paint;
                UpdateAllEye();
            }
            e.Handled = true;
            return;
        }
        if (!_marquee || _vm == null)
            return;
        var pos = e.GetPosition(RowList);
        var rect = new Rect(Math.Min(pos.X, _marqueeStart.X), Math.Min(pos.Y, _marqueeStart.Y),
            Math.Abs(pos.X - _marqueeStart.X), Math.Abs(pos.Y - _marqueeStart.Y));
        var host = Marquee.GetVisualParent() as Visual ?? this;
        var topLeft = RowList.TranslatePoint(rect.TopLeft, host) ?? rect.TopLeft;
        Marquee.Margin = new Thickness(topLeft.X, topLeft.Y, 0, 0);
        Marquee.Width = rect.Width;
        Marquee.Height = rect.Height;
        Marquee.IsVisible = rect.Width > 2 || rect.Height > 2;

        // Rows have a fixed height: the rectangle covers a range of indices.
        double offset = RowList.Scroll?.Offset.Y ?? 0;
        int first = Math.Max(0, (int)Math.Floor((rect.Top + offset) / RowHeight));
        int last = Math.Min(_vm.Hierarchy.Rows.Count - 1, (int)Math.Floor((rect.Bottom + offset) / RowHeight));
        var elements = new List<SceneElement>(_marqueeBase);
        for (int i = first; i <= last; i++)
        {
            if (_vm.Hierarchy.Rows[i].Element is { } element && !elements.Contains(element))
                elements.Add(element);
        }
        _vm.Select(elements, SelectMode.Replace);
        e.Handled = true;
    }

    private void OnMarqueeReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_eyePaint != null)
        {
            _eyePaint = null;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }
        if (!_marquee)
            return;
        e.Pointer.Capture(null);
        EndMarquee();
    }

    private void EndMarquee()
    {
        _marquee = false;
        Marquee.IsVisible = false;
    }

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (RowFrom(e.Source)?.Element is { } element)
            _vm?.RequestFrame(element);
    }

    private bool _expanderCtrl;

    private void OnExpanderClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: HierarchyRow row })
            _vm?.Hierarchy.ToggleExpanded(row, _expanderCtrl);
    }

    private object? CreatePayload()
    {
        if (_vm == null || _pressedRow?.Element == null)
            return null;
        if (!_pressedRow.Element.IsSelected)
            _vm.Select(_pressedRow.Element);
        var elements = _vm.GetTopmostSelection();
        return elements.Count > 0 ? new HierarchyDragPayload(elements) : null;
    }

    // ================= CONTEXT MENU ================= //

    private void ShowContextMenu(SceneElement? element) => ShowContextMenu(element, RowList, null);

    /// <summary>
    /// The Hierarchy's context menu, also opened by a right click in the Scene: there, new elements are created at
    /// the clicked point (<paramref name="createAt"/>, level space) at the top level.
    /// </summary>
    public void ShowContextMenu(SceneElement? element, Control target, Avalonia.Point? createAt)
    {
        if (_vm == null)
            return;
        var parentFor = createAt != null ? null : element;
        var vm = _vm;
        vm.Hierarchy.ContextElement = element;

        var create = new MenuItem { Header = "Create New", Icon = Icon("Icon.Plus") };
        foreach (var type in ElementTypeInfo.MenuOrder)
        {
            var item = new MenuItem { Header = type.ToString(), Icon = new PathIcon { Data = ElementTypeInfo.GetIcon(type) } };
            if (type == ElementType.Item)
            {
                foreach (var itemType in new[] { ItemType.Bonus, ItemType.Coin })
                {
                    var sub = new MenuItem { Header = itemType.ToString() };
                    sub.Click += async (_, _) => await vm.CreateFromMenuAsync(ElementType.Item, parentFor, itemType, createAt);
                    item.Items.Add(sub);
                }
            }
            else
            {
                item.Click += async (_, _) => await vm.CreateFromMenuAsync(type, parentFor, null, createAt);
            }
            create.Items.Add(item);
        }

        var menu = new ContextMenu();
        menu.Items.Add(create);

        if (element != null)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Command("Cut", "Icon.Cut", "Ctrl+X", () => vm.CutCommand.Execute(null)));
            menu.Items.Add(Command("Copy", "Icon.Copy", "Ctrl+C", () => vm.CopyCommand.Execute(null)));
            var paste = Command("Paste", "Icon.Paste", "Ctrl+V", () => vm.PasteAt(element));
            paste.IsEnabled = vm.HasClipboard;
            menu.Items.Add(paste);
            var pasteInto = Command("Paste Into", null, "Ctrl+Shift+V", () =>
            {
                if (element is ObjectElement obj)
                    vm.PasteInto(obj, -1);
            });
            pasteInto.IsEnabled = vm.HasClipboard && element is ObjectElement;
            menu.Items.Add(pasteInto);
            menu.Items.Add(Command("Delete", "Icon.Delete", "Delete", () => vm.DeleteCommand.Execute(null)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Command("Group", "Icon.Group", "Ctrl+G", () => vm.GroupCommand.Execute(null)));
            if (element is ObjectElement)
            {
                if (!element.IsSelected)
                    vm.Select(element);
                menu.Items.Add(Command("Ungroup", null, "Ctrl+Shift+G", () => vm.Ungroup()));
            }
            if (element is ImageElement)
            {
                if (!element.IsSelected)
                    vm.Select(element);
                int images = vm.Selection.Count(x => x is ImageElement);
                menu.Items.Add(Command(images > 1 ? $"Merge & Edit {images} Images" : "Edit Image", "Icon.Brush", "",
                    () => _ = vm.Paint.EditSelectionAsync()));
            }
            if (element is ObjectElement { Reference: not null } defined)
                menu.Items.Add(Command("Edit Object", "Icon.Object", "", () => _ = vm.EditObjectAsync(defined)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Command("Frame in Scene", "Icon.Crosshair", "F", () => vm.RequestFrame(element)));
        }

        menu.Open(target);
    }

    private static PathIcon? Icon(string key) =>
        Application.Current != null && Application.Current.TryGetResource(key, null, out var res) && res is Avalonia.Media.StreamGeometry g
            ? new PathIcon { Data = g }
            : null;

    private static MenuItem Command(string header, string? icon, string gesture, Action action)
    {
        var item = new MenuItem { Header = header, InputGesture = string.IsNullOrEmpty(gesture) ? null : KeyGesture.Parse(gesture) };
        if (icon != null)
            item.Icon = Icon(icon);
        item.Click += (_, _) => action();
        return item;
    }

    // ================= DROP (reorder / re-parent) ================= //

    private (HierarchyRow? Row, HierarchyDropPosition Position, Border? Border) RowAt(Point position)
    {
        var hit = RowList.InputHitTest(position) as Visual;
        while (hit != null && hit is not Border { Tag: HierarchyRow })
            hit = hit.GetVisualParent();
        if (hit is not Border { Tag: HierarchyRow row } border)
            return (null, HierarchyDropPosition.After, null);

        var local = RowList.TranslatePoint(position, border) ?? new Point();
        double h = border.Bounds.Height;
        var pos = local.Y < h * 0.25 ? HierarchyDropPosition.Before
            : local.Y > h * 0.75 ? HierarchyDropPosition.After
            : row.Element is ObjectElement ? HierarchyDropPosition.Inside
            : local.Y < h / 2 ? HierarchyDropPosition.Before : HierarchyDropPosition.After;
        if (row.IsRoot)
            pos = HierarchyDropPosition.Inside;
        return (row, pos, border);
    }

    public bool DragOver(object payload, Point position)
    {
        if (payload is PwItem { CanPlace: true })
        {
            // Project Window assets drop into an Object without definition (or at the top level).
            ClearDropFeedback();
            var (prow, _, pborder) = RowAt(this.TranslatePoint(position, RowList) ?? position);
            if (prow?.Element is ObjectElement { Reference: null } && pborder != null)
            {
                pborder.Classes.Add("dropinside");
                _dropRow = pborder;
            }
            return true;
        }
        if (payload is not HierarchyDragPayload)
            return false;
        ClearDropFeedback();
        var (row, pos, border) = RowAt(this.TranslatePoint(position, RowList) ?? position);
        if (border != null)
        {
            if (pos == HierarchyDropPosition.Inside && !(row?.IsRoot ?? false))
            {
                border.Classes.Add("dropinside");
                _dropRow = border;
            }
            else if (!(row?.IsRoot ?? false))
            {
                var top = border.TranslatePoint(new Point(0, pos == HierarchyDropPosition.Before ? 0 : border.Bounds.Height), DropLine.GetVisualParent() as Visual ?? this) ?? default;
                DropLine.Margin = new Thickness(0, Math.Max(0, top.Y - 1), 0, 0);
                DropLine.IsVisible = true;
            }
        }
        return true;
    }

    public void DragLeave() => ClearDropFeedback();

    private void ClearDropFeedback()
    {
        _dropRow?.Classes.Remove("dropinside");
        _dropRow = null;
        DropLine.IsVisible = false;
    }

    public async void Drop(object payload, Point position, KeyModifiers modifiers)
    {
        ClearDropFeedback();
        if (_vm != null && payload is PwItem { CanPlace: true } asset)
        {
            var (prow, _, _) = RowAt(this.TranslatePoint(position, RowList) ?? position);
            var parent = prow?.Element as ObjectElement;
            if (parent is { Reference: not null })
                parent = null; // objects with a definition are edited with Edit Object
            _vm.ProjectWindow.Select(asset);
            var type = _vm.ProjectWindow.ElementType;
            var at = parent?.WorldPosition ?? _vm.SceneViewCenter;
            var created = await _vm.CreateElementAsync(type, at, null, ProjectWindowViewModel.SourceFor(asset), parent);
            if (created != null)
            {
                if (parent != null)
                    parent.IsExpanded = true;
                _vm.Select(created);
            }
            _vm.ExitPlacement();
            return;
        }
        if (_vm == null || payload is not HierarchyDragPayload drag)
            return;
        var (row, pos, _) = RowAt(this.TranslatePoint(position, RowList) ?? position);
        if (row == null || row.IsRoot)
            _vm.Hierarchy.Drop(drag.Elements, null, HierarchyDropPosition.After);
        else
            _vm.Hierarchy.Drop(drag.Elements, row, pos);
    }
}
