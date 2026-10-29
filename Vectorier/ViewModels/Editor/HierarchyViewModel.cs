using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Vectorier.Models.Scene;

namespace Vectorier.ViewModels.Editor;

/// <summary>A visible line of the Hierarchy (the tree is flattened so long levels stay fast).</summary>
public sealed class HierarchyRow : ObservableObject
{
    private bool _hasChildren;
    private bool _isReference;

    public HierarchyRow(SceneElement? element, int depth, string? rootName = null)
    {
        Element = element;
        Depth = depth;
        RootName = rootName;
        _hasChildren = ComputeHasChildren();
        _isReference = Element is ObjectElement { IsReference: true };
    }

    /// <summary>Null for the root line showing the level name.</summary>
    public SceneElement? Element { get; }

    public string? RootName { get; }

    public bool IsRoot => Element == null;

    public int Depth { get; }

    public Avalonia.Thickness Indent => new(4 + Depth * 14, 0, 0, 0);

    public bool HasChildren => _hasChildren;

    public bool IsReference => _isReference;

    public StreamGeometry? Icon => Element == null ? ElementTypeInfo.GetIcon(ElementType.Object) : ElementTypeInfo.GetIcon(Element.Type);

    private bool ComputeHasChildren() => Element is ObjectElement obj && obj.Children.Count > 0;

    /// <summary>A reused row picks up changes of its element (children added, reference resolved).</summary>
    internal void Refresh()
    {
        bool has = ComputeHasChildren();
        if (has != _hasChildren)
        {
            _hasChildren = has;
            OnPropertyChanged(nameof(HasChildren));
        }
        bool reference = Element is ObjectElement { IsReference: true };
        if (reference != _isReference)
        {
            _isReference = reference;
            OnPropertyChanged(nameof(IsReference));
        }
    }
}

/// <summary>Scene structure panel: the level name as root, then every element (inline content only).</summary>
public sealed partial class HierarchyViewModel : ObservableObject
{
    private readonly EditorViewModel _editor;
    private bool _rebuildScheduled;

    public HierarchyViewModel(EditorViewModel editor)
    {
        _editor = editor;
        editor.StructureChanged += Rebuild;
    }

    /// <summary>
    /// The visible rows. Updated in place (only the rows that changed are inserted or removed), so adding an
    /// element to a long level doesn't recreate every row of the list.
    /// </summary>
    public Avalonia.Collections.AvaloniaList<HierarchyRow> Rows { get; } = new();

    private readonly Dictionary<SceneElement, HierarchyRow> _rowCache = new(ReferenceEqualityComparer.Instance);
    private HierarchyRow? _rootRow;

    [ObservableProperty] private string _filterText = string.Empty;

    /// <summary>Raised to scroll a row into view.</summary>
    public event Action<int>? ScrollRequested;

    /// <summary>The element whose context menu is open (Create New / Paste Into target).</summary>
    public SceneElement? ContextElement { get; set; }

    /// <summary>Anchor for Shift+click range selection.</summary>
    private SceneElement? _anchor;

    partial void OnFilterTextChanged(string value) => Rebuild();

    /// <summary>Rebuilds the rows on the next dispatcher pass (coalesces many changes into one rebuild).</summary>
    public void Rebuild()
    {
        if (_rebuildScheduled)
            return;
        _rebuildScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _rebuildScheduled = false;
            RebuildNow();
        }, DispatcherPriority.Background);
    }

    public void RebuildNow()
    {
        string levelName = _editor.ObjectEdit is { } edit ? $"Object: {edit.Name}" : _editor.Document.LevelName;
        if (_rootRow == null || _rootRow.RootName != levelName)
            _rootRow = new HierarchyRow(null, 0, levelName);
        var rows = new List<HierarchyRow> { _rootRow };
        var used = new Dictionary<SceneElement, HierarchyRow>(ReferenceEqualityComparer.Instance);
        string filter = FilterText.Trim();
        if (filter.Length > 0)
        {
            foreach (var element in _editor.Document.AllElements())
            {
                if (element is not BuildingMarkerElement && element.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    rows.Add(RowFor(element, 1, used));
            }
        }
        else
        {
            // Building In / Out markers are selected in the Scene only.
            foreach (var root in _editor.Document.Roots)
                if (root is not BuildingMarkerElement)
                    AddRows(rows, root, 1, used);
        }

        _rowCache.Clear();
        foreach (var pair in used)
            _rowCache[pair.Key] = pair.Value;
        Apply(rows);
    }

    /// <summary>Reuses the previous row of an element when its depth is unchanged (its container is kept).</summary>
    private HierarchyRow RowFor(SceneElement element, int depth, Dictionary<SceneElement, HierarchyRow> used)
    {
        if (_rowCache.TryGetValue(element, out var row) && row.Depth == depth)
            row.Refresh();
        else
            row = new HierarchyRow(element, depth);
        used[element] = row;
        return row;
    }

    private void AddRows(List<HierarchyRow> rows, SceneElement element, int depth, Dictionary<SceneElement, HierarchyRow> used)
    {
        rows.Add(RowFor(element, depth, used));
        if (element is ObjectElement obj && obj.IsExpanded)
        {
            foreach (var child in obj.Children)
                AddRows(rows, child, depth + 1, used);
        }
    }

    /// <summary>Replaces only the changed middle part of the list (common start and end are kept).</summary>
    private void Apply(List<HierarchyRow> rows)
    {
        int oldCount = Rows.Count, newCount = rows.Count;
        int prefix = 0;
        while (prefix < oldCount && prefix < newCount && ReferenceEquals(Rows[prefix], rows[prefix]))
            prefix++;
        int suffix = 0;
        while (suffix < oldCount - prefix && suffix < newCount - prefix &&
               ReferenceEquals(Rows[oldCount - 1 - suffix], rows[newCount - 1 - suffix]))
            suffix++;

        int removeCount = oldCount - prefix - suffix;
        int insertCount = newCount - prefix - suffix;
        if (removeCount > 0)
            Rows.RemoveRange(prefix, removeCount);
        if (insertCount > 0)
            Rows.InsertRange(prefix, rows.GetRange(prefix, insertCount));
    }

    public void ScrollTo(SceneElement element)
    {
        Dispatcher.UIThread.Post(() =>
        {
            for (int i = 0; i < Rows.Count; i++)
            {
                if (ReferenceEquals(Rows[i].Element, element))
                {
                    ScrollRequested?.Invoke(i);
                    return;
                }
            }
        }, DispatcherPriority.Background);
    }

    /// <param name="recursive">Ctrl held: every descendant object expands / collapses with it.</param>
    public void ToggleExpanded(HierarchyRow row, bool recursive = false)
    {
        if (row.Element is ObjectElement obj)
        {
            bool expand = !obj.IsExpanded;
            using (_editor.SuppressTracking())
            {
                if (recursive)
                {
                    foreach (var e in SceneDocument.Walk(obj))
                        if (e is ObjectElement o)
                            o.IsExpanded = expand;
                }
                else
                {
                    obj.IsExpanded = expand;
                }
            }
            Rebuild();
        }
    }

    /// <summary>Click on a row: plain = select, Ctrl = toggle, Shift = range.</summary>
    public void Click(HierarchyRow row, bool ctrl, bool shift, bool rightButton)
    {
        if (row.Element == null)
        {
            if (!rightButton)
                _editor.ClearSelection();
            return;
        }

        var element = row.Element;
        if (rightButton)
        {
            // Right click keeps a multi-selection that contains the element.
            if (!element.IsSelected)
                _editor.Select(element);
            return;
        }

        if (shift && _anchor != null)
        {
            int a = IndexOf(_anchor), b = IndexOf(element);
            if (a >= 0 && b >= 0)
            {
                var range = Rows.Skip(Math.Min(a, b)).Take(Math.Abs(a - b) + 1).Where(r => r.Element != null).Select(r => r.Element!).ToList();
                _editor.Select(range, ctrl ? SelectMode.Add : SelectMode.Replace);
                return;
            }
        }

        _anchor = element;
        _editor.Select(element, ctrl ? SelectMode.Toggle : SelectMode.Replace);
    }

    private int IndexOf(SceneElement element)
    {
        for (int i = 0; i < Rows.Count; i++)
        {
            if (ReferenceEquals(Rows[i].Element, element))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Drops dragged elements relative to a row: before/after it, or inside when it is an Object.
    /// </summary>
    public void Drop(IReadOnlyList<SceneElement> dragged, HierarchyRow? target, HierarchyDropPosition position)
    {
        if (dragged.Count == 0)
            return;

        // Only move the topmost dragged elements.
        var set = dragged.ToHashSet();
        var list = dragged.Where(e => !AncestorIn(e, set)).ToList();

        if (target?.Element == null)
        {
            _editor.MoveElements(list, null, -1);
            return;
        }

        var element = target.Element;
        if (list.Contains(element))
            return;

        if (position == HierarchyDropPosition.Inside && element is ObjectElement obj)
        {
            obj.IsExpanded = true;
            _editor.MoveElements(list, obj, -1);
            return;
        }

        var siblings = _editor.Document.SiblingsOf(element);
        int index = siblings.IndexOf(element) + (position == HierarchyDropPosition.After ? 1 : 0);
        _editor.MoveElements(list, element.Parent, index);
    }

    private static bool AncestorIn(SceneElement element, HashSet<SceneElement> set)
    {
        var p = element.Parent;
        while (p != null)
        {
            if (set.Contains(p))
                return true;
            p = p.Parent;
        }
        return false;
    }
}

public enum HierarchyDropPosition
{
    Before,
    Inside,
    After
}
