using System;
using System.Collections.Generic;
using System.Linq;
using Vectorier.Models.Scene;

namespace Vectorier.ViewModels.Editor;

public partial class DynamicEditorViewModel
{
    private sealed record TrackState(int ElementId, List<DynamicKey> Keys, int[] SelectedFrames, int TotalFrames, string Name,
        bool Modified, DynamicKey Pose);

    private sealed record State(int Frame, List<TrackState> Tracks);

    /// <summary>
    /// The timelines for an undo step: keys, key selection, lengths, picked transformation, unsaved flag, the playhead
    /// and each element's current pose (a move not keyed yet comes back too). Null while nothing is being edited.
    /// </summary>
    public object? CaptureState()
    {
        if (Tracks.Count == 0)
            return null;
        return new State(Frame, Tracks.Select(t => new TrackState(t.ElementId, t.Keys.Select(k => k.Clone()).ToList(),
            t.SelectedFrames.ToArray(), t.TotalFrames, t.TransformationName, t.IsModified, Capture(t.Element, 0))).ToList());
    }

    /// <summary>Puts the timelines of an undo step back (on the elements being edited now).</summary>
    public void RestoreState(object? state)
    {
        if (state is not State s || Tracks.Count == 0)
            return;
        StopPreview();
        var restored = new List<(DynamicTrack Track, DynamicKey Pose)>();
        foreach (var ts in s.Tracks)
        {
            var track = Tracks.FirstOrDefault(t => t.ElementId == ts.ElementId);
            if (track == null)
                continue;
            track.Keys.Clear();
            track.Keys.AddRange(ts.Keys.Select(k => k.Clone()));
            track.SelectedFrames.Clear();
            track.SelectedFrames.UnionWith(ts.SelectedFrames);
            track.TotalFrames = ts.TotalFrames;
            track.TransformationName = ts.Name;
            RefreshNames(track);
            track.IsModified = ts.Modified;
            Share(track);
            restored.Add((track, ts.Pose));
        }
        int frame = Math.Clamp(s.Frame, 0, MaxEnd);
        if (Frame != frame)
            Frame = frame;
        ApplyFrameToAll();
        foreach (var (track, pose) in restored)
            Apply(track.Element, pose);
        RaiseTimeline();
    }

    // ================= AUTO KEY ================= //

    private DynamicTransformKind _transformKind;

    /// <summary>The Scene reports rotations and scales, so Auto Key can skip what an element cannot animate.</summary>
    public void NoteTransform(DynamicTransformKind kind) => _transformKind = kind;

    /// <summary>
    /// Called when a change becomes an undo step (the pointer was released, a hotkey transform confirmed, a field
    /// committed): with Auto Key on, every edited element whose pose differs from its timeline is keyed at the
    /// playhead. Playback and scrubbing never get here (they do not record changes).
    /// </summary>
    public void OnChangeCommitted()
    {
        var kind = _transformKind;
        _transformKind = DynamicTransformKind.None;
        if (!AutoKey || Tracks.Count == 0 || IsPlaying)
            return;
        bool any = false;
        foreach (var track in Tracks)
        {
            var e = track.Element;
            // Rotating something that cannot rotate (it only orbits in a multi-selection), or scaling something
            // without a size, is not keyed.
            if (kind == DynamicTransformKind.Rotate && e is not ImageElement)
                continue;
            if (kind == DynamicTransformKind.Scale && !e.HasSize)
                continue;
            int frame = TrackFrame(track);
            var expected = Evaluate(track, frame);
            var now = Capture(e, frame);
            bool moved = Math.Abs(now.X - expected.X) > Eps || Math.Abs(now.Y - expected.Y) > Eps;
            bool sized = e.HasSize && (Math.Abs(now.Width - expected.Width) > Eps || Math.Abs(now.Height - expected.Height) > Eps);
            bool styled = e is ImageElement && (Math.Abs(now.Rotation - expected.Rotation) > Eps || now.Color != expected.Color);
            if (!moved && !sized && !styled)
                continue;
            if (!e.HasSize)
            {
                now.Width = expected.Width;
                now.Height = expected.Height;
            }
            if (track.KeyAt(frame) is { } existing)
            {
                now.Ease = existing.Ease;
                now.SupportX = existing.SupportX;
                now.SupportY = existing.SupportY;
            }
            Upsert(track, now);
            track.Sort();
            track.IsModified = true;
            Share(track);
            any = true;
        }
        if (!any)
            return;
        ApplyFrameToAll();
        RaiseTimeline();
    }
}
