using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Services;
using Vectorier.Services.Localization;

namespace Vectorier.ViewModels.Editor;

public partial class DynamicEditorViewModel
{
    [RelayCommand]
    public void TogglePlay()
    {
        if (Tracks.Count == 0)
            return;
        IsPlaying = !IsPlaying;
        _lastTick = DateTime.UtcNow;
        _carry = 0;
        if (IsPlaying)
        {
            if (Frame >= MaxEnd)
                Frame = 0;
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    public void StopPreview()
    {
        IsPlaying = false;
        _timer.Stop();
    }

    private void Tick()
    {
        if (!IsPlaying)
            return;
        var now = DateTime.UtcNow;
        double dt = (now - _lastTick).TotalSeconds;
        _lastTick = now;
        _carry += dt * Math.Max(1, Fps);
        int advance = (int)_carry;
        if (advance <= 0)
            return;
        _carry -= advance;
        int next = Frame + advance;
        if (next >= MaxEnd)
        {
            Frame = MaxEnd;
            StopPreview();
        }
        else
        {
            Frame = next;
        }
        TimelineChanged?.Invoke();
    }

    /// <summary>Frame step; the parameter comes from XAML as a string ("-5", "1"...).</summary>
    [RelayCommand]
    private void Step(string? deltaText)
    {
        if (!int.TryParse(deltaText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int delta)) return;
        StopPreview();
        Frame = Math.Clamp(Frame + delta, 0, MaxEnd);
        TimelineChanged?.Invoke();
    }

    public void SetFrame(int frame)
    {
        Frame = Math.Clamp(frame, 0, MaxEnd);
        TimelineChanged?.Invoke();
    }

    // ================= KEYFRAMES ================= //

    /// <summary>Stores a key, deriving the ease from the support like Unity's Upsert.</summary>
    private static void Upsert(DynamicTrack track, DynamicKey key, bool replace = true)
    {
        bool in01 = key.SupportX >= -Eps && key.SupportX <= 1 + Eps && key.SupportY >= -Eps && key.SupportY <= 1 + Eps;
        if (key.Ease != EasePreset.Custom || in01)
        {
            bool eq = Math.Abs(key.SupportX - key.SupportY) <= 0.001;
            key.Ease = eq && Math.Abs(key.SupportX) <= 0.001 ? EasePreset.EaseIn
                : eq && Math.Abs(key.SupportX - 0.5) <= 0.001 ? EasePreset.Linear
                : eq && Math.Abs(key.SupportX - 1) <= 0.001 ? EasePreset.EaseOut
                : key.Ease;
        }
        int index = track.Keys.FindIndex(k => k.Frame == key.Frame);
        if (index >= 0)
        {
            if (replace)
                track.Keys[index] = key;
            return;
        }
        track.Keys.Add(key);
        track.Sort();
    }

    [RelayCommand]
    private void AddKey(DynamicTrack? track)
    {
        track ??= ActiveTrack;
        if (track == null) return;
        ActiveTrack = track;
        var existing = track.KeyAt(TrackFrame(track));
        var key = Capture(track.Element, TrackFrame(track));
        if (existing != null)
        {
            key.Ease = existing.Ease;
            key.SupportX = existing.SupportX;
            key.SupportY = existing.SupportY;
        }
        Upsert(track, key);
        KeysChanged(track);
    }

    [RelayCommand]
    private void DeleteKey(DynamicTrack? track)
    {
        track ??= ActiveTrack;
        if (track == null) return;
        ActiveTrack = track;
        if (track.SelectedFrames.Count > 0)
        {
            track.Keys.RemoveAll(k => track.SelectedFrames.Contains(k.Frame));
            track.SelectedFrames.Clear();
        }
        else
        {
            track.Keys.RemoveAll(k => k.Frame == TrackFrame(track));
        }
        KeysChanged(track);
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task ClearKeys(DynamicTrack? track)
    {
        track ??= ActiveTrack;
        if (track == null) return;
        if (!await AppServices.Dialogs.ConfirmAsync(Loc.T("Clear Timeline"), Loc.F("Clear all keyframes on '{0}'?", track.Header), Loc.T("Clear")))
            return;
        track.Keys.Clear();
        track.SelectedFrames.Clear();
        KeysChanged(track);
    }

    public void CopyKeys()
    {
        var track = ActiveTrack;
        if (track == null) return;
        var frames = track.SelectedFrames.Count > 0 ? track.SelectedFrames.ToList() : new List<int> { TrackFrame(track) };
        var keys = track.Keys.Where(k => frames.Contains(k.Frame)).OrderBy(k => k.Frame).ToList();
        if (keys.Count == 0) return;
        Clipboard.Clear();
        foreach (var k in keys)
            Clipboard.Add((k.Frame - keys[0].Frame, k.Clone()));
        _editor.StatusText = Loc.F("Copied {0} keyframe(s)", keys.Count);
    }

    public void PasteKeys()
    {
        var track = ActiveTrack;
        if (track == null || Clipboard.Count == 0) return;
        PlaceKeys(track, Clipboard, TrackFrame(track));
    }

    /// <summary>Cut: copies the selected keys (or the key at the playhead), then deletes them.</summary>
    public void CutKeys()
    {
        var track = ActiveTrack;
        if (track == null) return;
        var frames = track.SelectedFrames.Count > 0 ? track.SelectedFrames.ToHashSet() : new HashSet<int> { TrackFrame(track) };
        if (!track.Keys.Any(k => frames.Contains(k.Frame))) return;
        CopyKeys();
        track.Keys.RemoveAll(k => frames.Contains(k.Frame));
        track.SelectedFrames.Clear();
        KeysChanged(track);
    }

    /// <summary>
    /// Duplicate: copies of the selected keys (or the key at the playhead) placed after them, keeping their spacing,
    /// with a gap of <see cref="DuplicateGap"/> frames. The copies are selected so they can be dragged into place.
    /// </summary>
    public void DuplicateKeys()
    {
        var track = ActiveTrack;
        if (track == null) return;
        var frames = track.SelectedFrames.Count > 0 ? track.SelectedFrames.ToList() : new List<int> { TrackFrame(track) };
        var keys = track.Keys.Where(k => frames.Contains(k.Frame)).OrderBy(k => k.Frame).ToList();
        if (keys.Count == 0) return;
        var copies = keys.Select(k => (k.Frame - keys[0].Frame, k.Clone())).ToList();
        int start = keys[^1].Frame + DuplicateGap;
        PlaceKeys(track, copies, start);
        SetFrame(start);
    }

    /// <summary>Frames between the last duplicated key and the first copy.</summary>
    public const int DuplicateGap = 10;

    /// <summary>Puts keys (frame offsets from <paramref name="at"/>) on the track, lengthening it if they go past its end.</summary>
    private void PlaceKeys(DynamicTrack track, IReadOnlyList<(int Df, DynamicKey Key)> keys, int at)
    {
        track.SelectedFrames.Clear();
        int last = at + keys.Max(k => k.Df);
        if (last > track.TotalFrames)
            track.TotalFrames = last;
        foreach (var (df, k) in keys)
        {
            var key = k.Clone();
            key.Frame = Math.Max(0, at + df);
            Upsert(track, key);
            track.SelectedFrames.Add(key.Frame);
        }
        KeysChanged(track);
    }

    public void SelectAllKeys()
    {
        var track = ActiveTrack;
        if (track == null) return;
        track.SelectedFrames.Clear();
        foreach (var k in track.Keys)
            track.SelectedFrames.Add(k.Frame);
        RaiseTimeline();
    }

    /// <summary>Moves keys by a frame delta (timeline drag). Moved keys win over stationary ones on the same frame.</summary>
    public void MoveKeys(DynamicTrack track, IReadOnlyList<(DynamicKey Key, int OriginalFrame)> moving, int delta, bool merge)
    {
        foreach (var (key, original) in moving)
            key.Frame = Math.Clamp(original + delta, 0, track.TotalFrames);
        track.SelectedFrames.Clear();
        foreach (var (key, _) in moving)
            track.SelectedFrames.Add(key.Frame);

        if (merge)
        {
            var movedSet = moving.Select(m => m.Key).ToHashSet();
            var byFrame = new Dictionary<int, DynamicKey>();
            foreach (var k in track.Keys.Where(k => !movedSet.Contains(k)))
                byFrame[k.Frame] = k;
            foreach (var k in movedSet)
                byFrame[k.Frame] = k;
            track.Keys.Clear();
            track.Keys.AddRange(byFrame.Values);
        }
        KeysChanged(track);
    }

    public void SetTotalFrames(DynamicTrack track, int total)
    {
        track.TotalFrames = Math.Max(1, total);
        if (Frame > MaxEnd) Frame = MaxEnd;
        track.IsModified = true;
        RaiseTimeline();
        _editor.MarkChanged();
    }
}
