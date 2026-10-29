using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Models.Scene;

namespace Vectorier.ViewModels.Editor;

/// <summary>
/// Undo/redo. Every committed change stores a snapshot of the whole scene (Unity-like "record object" for the
/// level). Changes are detected automatically from element property notifications and coalesced: a drag in
/// the Scene, a slider or a value typed in a field is a single undo step, committed once the input is finished
/// (pointer released, or a short pause after typing). Selection changes are steps too (they share the scene
/// snapshot, so they cost almost nothing). The Hierarchy's expanded rows survive undo and redo.
/// </summary>
public sealed partial class EditorViewModel
{
    private const int MaxUndo = 100;

    private sealed record HistoryEntry(SceneDocument.Snapshot State, int Version, int[] Selection);

    private readonly List<HistoryEntry> _undo = new();
    private readonly List<HistoryEntry> _redo = new();
    private HistoryEntry? _baseline;
    private int _version;
    private int _versionCounter;
    private int _savedVersion;
    private bool _pending;
    private int _gestureDepth;
    private int _suppressTracking;
    private bool _selectionPending;
    private bool _restoring;
    private DispatcherTimer? _commitTimer;

    /// <summary>Pause after the last change before it becomes an undo step (typing in a field, wheel...).</summary>
    private static readonly TimeSpan CommitDelay = TimeSpan.FromMilliseconds(450);
    private readonly HashSet<SceneElement> _tracked = new();

    public bool CanUndo => _undo.Count > 0 || _pending || _selectionPending;
    public bool CanRedo => _redo.Count > 0;

    private void ResetHistory()
    {
        _undo.Clear();
        _redo.Clear();
        _pending = false;
        _selectionPending = false;
        _gestureDepth = 0;
        _version = ++_versionCounter;
        _savedVersion = Document.HasLocation ? _version : -1;
        _baseline = CaptureEntry();
        IsDirty = !Document.HasLocation && Document.Roots.Count > 0;
        RaiseHistoryChanged();
    }

    private HistoryEntry CaptureEntry()
    {
        // Undo states never contain a pose previewed by the Dynamic Editor.
        using (Dynamics.SuspendPreview())
            return new(Document.CreateSnapshot(), _version, Selection.Select(e => e.Id).ToArray());
    }

    private void MarkSaved()
    {
        CommitPending();
        _savedVersion = _version;
        IsDirty = false;
    }

    /// <summary>Runs an action without recording it (render-time fills, selection state...).</summary>
    public IDisposable SuppressTracking()
    {
        _suppressTracking++;
        return new Releaser(() => _suppressTracking--);
    }

    private sealed class Releaser : IDisposable
    {
        private Action? _release;
        public Releaser(Action release) => _release = release;
        public void Dispose()
        {
            _release?.Invoke();
            _release = null;
        }
    }

    /// <summary>Records that the scene changed. The change becomes an undo step when the current gesture ends.</summary>
    public void MarkChanged()
    {
        if (_suppressTracking > 0)
            return;
        _pending = true;
        IsDirty = true;
        ScheduleCommit();
    }

    /// <summary>Selection changed: recorded as its own undo step (except while undoing / redoing).</summary>
    private void MarkSelectionChanged()
    {
        if (_restoring || _suppressTracking > 0 || _baseline == null)
            return;
        _selectionPending = true;
        ScheduleCommit();
    }

    /// <summary>Outside a gesture, commits after a short pause so a burst of changes is one step.</summary>
    private void ScheduleCommit()
    {
        if (_gestureDepth > 0)
            return;
        if (_commitTimer == null)
        {
            _commitTimer = new DispatcherTimer { Interval = CommitDelay };
            _commitTimer.Tick += (_, _) =>
            {
                _commitTimer!.Stop();
                if (_gestureDepth == 0)
                    CommitPending();
            };
        }
        _commitTimer.Stop();
        _commitTimer.Start();
    }

    /// <summary>Starts a continuous edit (drag, modal transform). All changes until <see cref="EndGesture"/> form one step.</summary>
    public void BeginGesture()
    {
        if (_gestureDepth == 0)
            CommitPending();
        _gestureDepth++;
    }

    public void EndGesture()
    {
        if (_gestureDepth == 0)
            return;
        _gestureDepth--;
        if (_gestureDepth == 0)
            CommitPending();
    }

    /// <summary>Ends the current gesture and reverts everything it changed.</summary>
    public void CancelGesture()
    {
        if (_gestureDepth == 0)
            return;
        _gestureDepth = 0;
        if (_pending && _baseline != null)
        {
            _pending = false;
            RestoreEntry(_baseline, full: true);
            IsDirty = _version != _savedVersion;
        }
    }

    public bool IsInGesture => _gestureDepth > 0;

    public void CommitPending()
    {
        _commitTimer?.Stop();
        // Live platform surfaces belong to the step of the change that caused them.
        if (_surfacePending.Count > 0 && !_surfaceFlushing)
            FlushSurfaces();
        if (_pending)
        {
            _pending = false;
            _selectionPending = false;
            if (_baseline != null)
            {
                _undo.Add(_baseline);
                if (_undo.Count > MaxUndo)
                    _undo.RemoveAt(0);
            }
            _redo.Clear();
            _version = ++_versionCounter;
            _baseline = CaptureEntry();
            IsDirty = _version != _savedVersion;
            RaiseHistoryChanged();
            return;
        }

        if (_selectionPending)
        {
            _selectionPending = false;
            var ids = Selection.Select(e => e.Id).ToArray();
            if (_baseline == null || ids.SequenceEqual(_baseline.Selection))
            {
                RaiseHistoryChanged();
                return;
            }
            // Same scene, other selection: the snapshot is shared.
            _undo.Add(_baseline);
            if (_undo.Count > MaxUndo)
                _undo.RemoveAt(0);
            _redo.Clear();
            _baseline = _baseline with { Selection = ids };
            RaiseHistoryChanged();
        }
    }

    [RelayCommand]
    public void Undo()
    {
        if (IsInGesture)
            return;
        CommitPending();
        if (_undo.Count == 0 || _baseline == null)
            return;
        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(_baseline);
        RestoreEntry(entry);
        StatusText = "Undo";
    }

    [RelayCommand]
    public void Redo()
    {
        if (IsInGesture)
            return;
        CommitPending();
        if (_redo.Count == 0 || _baseline == null)
            return;
        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(_baseline);
        RestoreEntry(entry);
        StatusText = "Redo";
    }

    private async void RestoreEntry(HistoryEntry entry, bool full = false)
    {
        _restoring = true;
        try
        {
            if (!full && _baseline != null && ReferenceEquals(entry.State, _baseline.State))
            {
                // Only the selection differs.
                _baseline = entry;
                ClearSelectionSilently();
                SelectByIds(entry.Selection);
                RaiseSelectionChanged();
                RaiseHistoryChanged();
                return;
            }

            var previousSets = (Document.Settings.CitySets.ToList(), Document.Settings.GroundSets.ToList());
            // The Hierarchy keeps what was expanded (elements are recreated from the snapshot).
            var expanded = Document.AllElements().Where(e => e.IsExpanded).Select(e => e.Id).ToHashSet();
            var hidden = Document.AllElements().Where(e => e.IsHidden).Select(e => e.Id).ToHashSet();
            using (SuppressTracking())
            {
                ClearSelectionSilently();
                Document.RestoreSnapshot(entry.State);
                foreach (var element in Document.AllElements())
                {
                    if (expanded.Contains(element.Id))
                        element.IsExpanded = true;
                    if (hidden.Contains(element.Id))
                        element.IsHidden = true;
                }
            }
            _baseline = entry;
            _version = entry.Version;
            IsDirty = _version != _savedVersion;
            RaiseStructureChanged();
            SelectByIds(entry.Selection);
            RaiseSelectionChanged();
            RaiseHistoryChanged();

            if (!previousSets.Item1.SequenceEqual(Document.Settings.CitySets) || !previousSets.Item2.SequenceEqual(Document.Settings.GroundSets))
            {
                await ResolveDefinitionsAsync(Document.Settings);
                using (SuppressTracking())
                    ResolveReferences();
                RaiseStructureChanged();
            }
        }
        finally
        {
            _restoring = false;
        }
    }

    private void RaiseHistoryChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    // ================= CHANGE TRACKING ================= //

    private static readonly HashSet<string> UiOnlyProperties = new(StringComparer.Ordinal)
    {
        nameof(SceneElement.IsSelected),
        nameof(SceneElement.IsExpanded),
        nameof(SceneElement.IsHidden),
        nameof(SceneElement.DisplayName),
        nameof(ObjectElement.Reference),
        nameof(ObjectElement.IsReference),
    };

    /// <summary>Re-subscribes to every element and notifies the Hierarchy and Scene.</summary>
    public void RaiseStructureChanged()
    {
        var current = new HashSet<SceneElement>(Document.AllElements());
        foreach (var element in _tracked)
        {
            if (!current.Contains(element))
                element.PropertyChanged -= OnElementPropertyChanged;
        }
        foreach (var element in current)
        {
            if (!_tracked.Contains(element))
                element.PropertyChanged += OnElementPropertyChanged;
        }
        _tracked.Clear();
        _tracked.UnionWith(current);

        // Drop selected elements that are no longer in the scene.
        if (Selection.Any(e => !current.Contains(e)))
        {
            foreach (var removed in Selection.Where(e => !current.Contains(e)).ToList())
            {
                removed.IsSelected = false;
                _selection.Remove(removed);
            }
            if (ActiveElement != null && !current.Contains(ActiveElement))
                ActiveElement = _selection.LastOrDefault();
            RaiseSelectionChanged();
        }

        StructureChanged?.Invoke();
        SceneInvalidated?.Invoke();
    }

    private void OnElementPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not SceneElement element)
            return;

        if (e.PropertyName != null && UiOnlyProperties.Contains(e.PropertyName))
        {
            if (e.PropertyName == nameof(SceneElement.IsExpanded))
                Hierarchy.Rebuild();
            else if (e.PropertyName == nameof(SceneElement.IsHidden))
                SceneInvalidated?.Invoke();
            return;
        }

        if (element is ObjectElement obj && e.PropertyName == nameof(SceneElement.Name))
        {
            obj.Reference = string.IsNullOrEmpty(obj.Name) ? null : _reader.ResolveDefinition(obj.Name);
            ElementChanged?.Invoke(element, e.PropertyName);
            StructureChanged?.Invoke();
            MarkChanged();
            SceneInvalidated?.Invoke();
            return;
        }

        ElementChanged?.Invoke(element, e.PropertyName);
        OnSurfacePropertyChanged(element, e.PropertyName);
        OnAreaPropertyChanged(element, e.PropertyName);
        MarkChanged();
        SceneInvalidated?.Invoke();
    }
}
