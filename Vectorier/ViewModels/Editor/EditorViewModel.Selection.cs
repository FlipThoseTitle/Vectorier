using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Models.Scene;
using Vectorier.Services.Localization;

namespace Vectorier.ViewModels.Editor;

public enum SelectMode
{
    Replace,
    Add,
    Toggle,
    Remove
}

public sealed partial class EditorViewModel
{
    private readonly List<SceneElement> _selection = new();

    /// <summary>Selected elements in selection order (the last one is the active element).</summary>
    public IReadOnlyList<SceneElement> Selection => _selection;

    /// <summary>The element shown in the Inspector (most recently selected).</summary>
    [ObservableProperty] private SceneElement? _activeElement;

    public bool HasSelection => _selection.Count > 0;

    public event Action? SelectionChanged;

    public void Select(SceneElement? element, SelectMode mode = SelectMode.Replace)
    {
        if (element == null)
        {
            if (mode == SelectMode.Replace)
                ClearSelection();
            return;
        }
        Select(new[] { element }, mode);
    }

    public void Select(IEnumerable<SceneElement> elements, SelectMode mode = SelectMode.Replace)
    {
        var list = elements.Distinct().ToList();
        if (mode == SelectMode.Replace)
        {
            if (list.Count == _selection.Count && list.SequenceEqual(_selection))
                return;
            ClearSelectionSilently();
        }

        foreach (var element in list)
        {
            bool selected = _selection.Contains(element);
            switch (mode)
            {
                case SelectMode.Replace:
                case SelectMode.Add:
                    if (!selected)
                    {
                        _selection.Add(element);
                        element.IsSelected = true;
                    }
                    else if (mode == SelectMode.Add)
                    {
                        // Re-adding makes it the active element.
                        _selection.Remove(element);
                        _selection.Add(element);
                    }
                    break;
                case SelectMode.Toggle:
                    if (selected)
                    {
                        _selection.Remove(element);
                        element.IsSelected = false;
                    }
                    else
                    {
                        _selection.Add(element);
                        element.IsSelected = true;
                    }
                    break;
                case SelectMode.Remove:
                    if (selected)
                    {
                        _selection.Remove(element);
                        element.IsSelected = false;
                    }
                    break;
            }
        }

        ActiveElement = _selection.LastOrDefault();
        RaiseSelectionChanged();
    }

    [RelayCommand]
    public void ClearSelection()
    {
        if (_selection.Count == 0)
            return;
        ClearSelectionSilently();
        RaiseSelectionChanged();
    }

    private void ClearSelectionSilently()
    {
        foreach (var element in _selection)
            element.IsSelected = false;
        _selection.Clear();
        ActiveElement = null;
    }

    private void SelectByIds(IEnumerable<int> ids)
    {
        var set = ids.ToHashSet();
        if (set.Count == 0)
            return;
        foreach (var element in Document.AllElements())
        {
            if (set.Contains(element.Id) && !_selection.Contains(element))
            {
                _selection.Add(element);
                element.IsSelected = true;
            }
        }
        ActiveElement = _selection.LastOrDefault();
    }

    private void RaiseSelectionChanged()
    {
        // Like Unity, selecting in the Scene reveals the element in the Hierarchy.
        if (ActiveElement != null)
            RevealInHierarchy(ActiveElement);
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(Selection));
        SelectionChanged?.Invoke();
        SceneInvalidated?.Invoke();
        MarkSelectionChanged();
        CutCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        GroupCommand.NotifyCanExecuteChanged();
        DuplicateCommand.NotifyCanExecuteChanged();
        PasteIntoCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Selected elements without those whose ancestor is also selected (what transforms and copies act on).</summary>
    public List<SceneElement> GetTopmostSelection()
    {
        var set = _selection.ToHashSet();
        return _selection.Where(e =>
        {
            var p = e.Parent;
            while (p != null)
            {
                if (set.Contains(p))
                    return false;
                p = p.Parent;
            }
            return true;
        }).ToList();
    }

    [RelayCommand]
    private void SelectAll() => Select(Document.Roots.Where(e => IsTypeVisible(e.Type)).ToList());

    /// <summary>Expands the Hierarchy down to the element so it becomes visible there.</summary>
    /// <summary>H: hides the selected elements in the Scene, or shows them again when they are all hidden.</summary>
    public void ToggleSelectionHidden()
    {
        var targets = GetTopmostSelection();
        if (targets.Count == 0)
            return;
        bool hide = targets.Any(e => !e.IsHidden);
        foreach (var e in targets)
            e.IsHidden = hide;
        SceneInvalidated?.Invoke();
        StatusText = hide ? Loc.F("Hidden ({0})", targets.Count) : Loc.F("Shown ({0})", targets.Count);
    }

    public void RevealInHierarchy(SceneElement element)
    {
        var p = element.Parent;
        bool changed = false;
        using (SuppressTracking())
        {
            while (p != null)
            {
                if (!p.IsExpanded)
                {
                    p.IsExpanded = true;
                    changed = true;
                }
                p = p.Parent;
            }
        }
        if (changed)
            Hierarchy.Rebuild();
        Hierarchy.ScrollTo(element);
    }
}
