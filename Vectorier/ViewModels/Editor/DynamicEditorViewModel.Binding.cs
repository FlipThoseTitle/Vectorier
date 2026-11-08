using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Models.Scene;
using Vectorier.Services.Localization;

namespace Vectorier.ViewModels.Editor;

public partial class DynamicEditorViewModel
{
    /// <summary>Binds the selected elements (Unity: "Edit Selected GameObject").</summary>
    [RelayCommand]
    public void BindSelection()
    {
        var elements = _editor.Selection.Where(e => e.SupportsDynamics).ToList();
        if (elements.Count == 0)
        {
            Unbind();
            return;
        }
        StopPreview();

        var keep = new List<DynamicTrack>();
        foreach (var e in elements)
        {
            var existing = Tracks.FirstOrDefault(t => ReferenceEquals(t.Element, e));
            keep.Add(existing ?? MakeTrack(e));
        }
        foreach (var dropped in Tracks.Where(t => !keep.Contains(t)).ToList())
            Restore(dropped);

        Tracks.Clear();
        foreach (var t in keep)
            Tracks.Add(t);
        ActiveTrack = Tracks.FirstOrDefault(t => ReferenceEquals(t.Element, _editor.ActiveElement)) ?? Tracks.FirstOrDefault();
        OnPropertyChanged(nameof(IsBound));
        OnPropertyChanged(nameof(SelectionMatches));
        Frame = 0;
        ApplyFrameToAll();
        RaiseTimeline();
        // Undo steps from now on know the timelines (binding itself is not a step).
        _editor.RefreshHistoryBaseline();
    }

    private DynamicTrack MakeTrack(SceneElement element)
    {
        var track = new DynamicTrack(element) { Original = Capture(element, 0) };
        RefreshNames(track);
        string first = track.TransformationNames.FirstOrDefault() ?? "Transform";
        track.TransformationName = first;
        LoadTransformation(track, first);
        return track;
    }

    /// <summary>
    /// Syncs the transformation list in place (clearing it would make the combo box lose its pick and show "(new)"),
    /// then re-sends the picked name.
    /// </summary>
    private static void RefreshNames(DynamicTrack track)
    {
        // Its own transformations, then its definition's (an Object instance runs both in the game).
        var names = track.Element.Dynamics.Select(d => N(d.Name))
            .Concat(track.Element.InheritedDynamics.Select(d => N(d.Name))).Distinct().ToList();
        var list = track.TransformationNames;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (!names.Contains(list[i]))
                list.RemoveAt(i);
        }
        for (int i = 0; i < names.Count; i++)
        {
            int at = list.IndexOf(names[i]);
            if (at < 0)
                list.Insert(i, names[i]);
            else if (at != i)
                list.Move(at, i);
        }
        track.NotifyTransformationName();
    }

    /// <summary>Exit button: offers to save timelines that are not baked yet, then exits.</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task ExitAsync()
    {
        if (!await ResolveUnsavedAsync(Loc.T("Exit Dynamic Editor")))
            return;
        Unbind();
    }

    /// <summary>Exits the Dynamic Editor: every element gets its rest pose back.</summary>
    [RelayCommand]
    public void Unbind()
    {
        StopPreview();
        foreach (var t in Tracks)
            Restore(t);
        Tracks.Clear();
        ActiveTrack = null;
        OnPropertyChanged(nameof(IsBound));
        OnPropertyChanged(nameof(SelectionMatches));
        RaiseTimeline();
        _editor.InvalidateScene();
    }

    /// <summary>
    /// Puts the element back at its rest pose. A first key moved by the user becomes the new rest pose
    /// (the saved transformation is relative to it).
    /// </summary>
    private void Restore(DynamicTrack track)
    {
        var rest = track.Keys.FirstOrDefault(k => k.Frame == 0) ?? track.Original;
        bool restChanged = !SamePose(rest, track.Original);
        if (restChanged)
        {
            var e = track.Element;
            e.X = rest.X;
            e.Y = rest.Y;
            if (e.HasSize && (Math.Abs(e.Width - rest.Width) > Eps || Math.Abs(e.Height - rest.Height) > Eps))
            {
                e.Width = rest.Width;
                e.Height = rest.Height;
            }
            if (e is ImageElement image)
            {
                image.Rotation = rest.Rotation;
                image.Color = rest.Color;
            }
        }
        else
        {
            Apply(track.Element, track.Original);
        }
    }

    private static bool SamePose(DynamicKey a, DynamicKey b) =>
        Math.Abs(a.X - b.X) < Eps && Math.Abs(a.Y - b.Y) < Eps && Math.Abs(a.Width - b.Width) < Eps && Math.Abs(a.Height - b.Height) < Eps &&
        Math.Abs(a.Rotation - b.Rotation) < Eps && a.Color == b.Color;

    /// <summary>After undo/redo the scene holds new element instances: re-attach tracks by id.</summary>
    private void RemapTracks()
    {
        if (Tracks.Count == 0)
            return;
        foreach (var t in Tracks.ToList())
        {
            var e = _editor.Document.FindById(t.ElementId);
            if (e == null)
                Tracks.Remove(t);
            else
                t.Element = e;
        }
        if (Tracks.Count == 0)
            ActiveTrack = null;
        OnPropertyChanged(nameof(IsBound));
        ApplyFrameToAll();
        RaiseTimeline();
    }

    /// <summary>
    /// Runs an action with every bound element at its rest pose (saving and undo snapshots never see a
    /// previewed pose), then puts the current pose back.
    /// </summary>
    public IDisposable SuspendPreview()
    {
        if (Tracks.Count == 0)
            return new Scope(null);
        var current = Tracks.Select(t => (t, Capture(t.Element, 0))).ToList();
        foreach (var t in Tracks)
            Apply(t.Element, t.Keys.FirstOrDefault(k => k.Frame == 0) ?? t.Original);
        return new Scope(() =>
        {
            foreach (var (t, pose) in current)
                Apply(t.Element, pose);
        });
    }

    private sealed class Scope : IDisposable
    {
        private Action? _end;
        public Scope(Action? end) => _end = end;
        public void Dispose()
        {
            _end?.Invoke();
            _end = null;
        }
    }
}
