using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Models.Scene;
using Vectorier.Services;
using Vectorier.Services.Localization;

namespace Vectorier.ViewModels.Editor;

public enum EasePreset
{
    EaseIn,
    Linear,
    EaseOut,
    Custom
}

/// <summary>A keyframe: the element's pose at a frame (level units, XML orientation).</summary>
public sealed class DynamicKey
{
    public int Frame;
    public double X, Y, Width, Height, Rotation;
    public Color Color = Colors.White;

    /// <summary>Bezier support relative to the previous key's position (used when <see cref="Ease"/> is Custom).</summary>
    public double SupportX, SupportY;

    public EasePreset Ease = EasePreset.Linear;

    public DynamicKey Clone() => (DynamicKey)MemberwiseClone();

    public static double PresetValue(EasePreset e) => e == EasePreset.EaseIn ? 0 : e == EasePreset.EaseOut ? 1 : 0.5;
}

/// <summary>One bound element in the Dynamic Editor with its keyframe timeline (Unity: DynamicTimelineData + DynamicPreview).</summary>
public sealed partial class DynamicTrack : ObservableObject
{
    public DynamicTrack(SceneElement element)
    {
        Element = element;
        ElementId = element.Id;
    }

    public SceneElement Element { get; set; }
    public int ElementId { get; }

    public List<DynamicKey> Keys { get; } = new();
    public HashSet<int> SelectedFrames { get; } = new();

    [ObservableProperty] private int _totalFrames = 300;
    [ObservableProperty] private string _transformationName = "Transform";
    [ObservableProperty] private bool _isActive;

    public ObservableCollection<string> TransformationNames { get; } = new();

    /// <summary>Pose before the editor touched the element; put back when it is unbound.</summary>
    public DynamicKey Original { get; set; } = new();

    public string Header => Element.DisplayName;

    /// <summary>Raised when keys change (the timeline redraws).</summary>
    public event Action? KeysChanged;

    public void RaiseKeysChanged() => KeysChanged?.Invoke();

    public void Sort() => Keys.Sort((a, b) => a.Frame.CompareTo(b.Frame));

    public DynamicKey? KeyAt(int frame) => Keys.FirstOrDefault(k => k.Frame == frame);
}

/// <summary>
/// Dynamic Editor: keyframe timelines for &lt;Transformation&gt;s (Move / Size / Rotation / Color). Keys are baked
/// into the element's transformations with Save, and loaded back from them, like Unity's Dynamic Editor.
/// While elements are bound, the playhead previews their pose in the Scene; unbinding restores them.
/// </summary>
public sealed partial class DynamicEditorViewModel : ObservableObject
{
    private const double Eps = 0.0005;

    private readonly EditorViewModel _editor;
    private readonly DispatcherTimer _timer;
    private DateTime _lastTick;
    private double _carry;
    private static readonly List<(int Df, DynamicKey Key)> Clipboard = new();

    public DynamicEditorViewModel(EditorViewModel editor)
    {
        _editor = editor;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(1000.0 / 60), DispatcherPriority.Render, (_, _) => Tick());
        editor.StructureChanged += RemapTracks;
        editor.SelectionChanged += () =>
        {
            OnPropertyChanged(nameof(SelectionMatches));
            OnPropertyChanged(nameof(CanBind));
            if (_editor.ActiveElement != null && Tracks.FirstOrDefault(t => ReferenceEquals(t.Element, _editor.ActiveElement)) is { } track)
                ActiveTrack = track;
        };
    }

    public ObservableCollection<DynamicTrack> Tracks { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveKey), nameof(HasActiveKey), nameof(ShowEaseCombo), nameof(ShowSupport))]
    private DynamicTrack? _activeTrack;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveKey), nameof(HasActiveKey), nameof(ShowEaseCombo), nameof(ShowSupport), nameof(FrameText))]
    private int _frame;

    [ObservableProperty] private bool _isPlaying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEaseCombo), nameof(ShowSupport))]
    private bool _customEase;

    [ObservableProperty] private bool _onion;
    [ObservableProperty] private int _fps = 60;

    /// <summary>Pixels per frame on the timeline.</summary>
    [ObservableProperty] private double _zoom = 1;

    /// <summary>First visible frame (shared by every track so playheads line up).</summary>
    [ObservableProperty] private double _scrollFrame;

    public static IReadOnlyList<EasePreset> EasePresets { get; } = Enum.GetValues<EasePreset>();

    public bool IsBound => Tracks.Count > 0;

    public bool CanBind => _editor.Selection.Any(e => e.SupportsDynamics);

    public bool SelectionMatches
    {
        get
        {
            var selected = _editor.Selection.Where(e => e.SupportsDynamics).ToList();
            return selected.Count == Tracks.Count && selected.All(e => Tracks.Any(t => ReferenceEquals(t.Element, e)));
        }
    }

    public int MaxEnd => Tracks.Count == 0 ? 1 : Math.Max(1, Tracks.Max(t => t.TotalFrames));

    public string FrameText => $"/ {MaxEnd}";

    public int TrackFrame(DynamicTrack track) => Math.Min(Frame, track.TotalFrames);

    public DynamicKey? ActiveKey => ActiveTrack?.KeyAt(TrackFrame(ActiveTrack));

    public bool HasActiveKey => ActiveKey != null;

    public bool ShowEaseCombo => HasActiveKey && !CustomEase;

    public bool ShowSupport => HasActiveKey && (CustomEase || ActiveKey!.Ease == EasePreset.Custom);

    public EasePreset ActiveEase
    {
        get => ActiveKey?.Ease ?? EasePreset.Linear;
        set
        {
            var key = ActiveKey;
            if (key == null || key.Ease == value) return;
            key.Ease = value;
            if (value != EasePreset.Custom)
            {
                key.SupportX = DynamicKey.PresetValue(value);
                key.SupportY = DynamicKey.PresetValue(value);
            }
            KeysChanged(ActiveTrack!);
            OnPropertyChanged(nameof(ShowSupport));
        }
    }

    public double SupportX
    {
        get => ActiveKey?.SupportX ?? 0;
        set
        {
            if (ActiveKey is not { } key || Math.Abs(key.SupportX - value) < 1e-9) return;
            key.SupportX = value;
            key.Ease = EasePreset.Custom;
            KeysChanged(ActiveTrack!);
        }
    }

    public double SupportY
    {
        get => -(ActiveKey?.SupportY ?? 0);
        set
        {
            if (ActiveKey is not { } key || Math.Abs(-key.SupportY - value) < 1e-9) return;
            key.SupportY = -value;
            key.Ease = EasePreset.Custom;
            KeysChanged(ActiveTrack!);
        }
    }

    partial void OnFrameChanged(int value)
    {
        ApplyFrameToAll();
        OnPropertyChanged(nameof(ActiveEase));
        OnPropertyChanged(nameof(SupportX));
        OnPropertyChanged(nameof(SupportY));
    }

    partial void OnActiveTrackChanged(DynamicTrack? oldValue, DynamicTrack? newValue)
    {
        if (oldValue != null) oldValue.IsActive = false;
        if (newValue != null) newValue.IsActive = true;
        OnPropertyChanged(nameof(ActiveEase));
        OnPropertyChanged(nameof(SupportX));
        OnPropertyChanged(nameof(SupportY));
        _editor.InvalidateScene();
    }

    partial void OnCustomEaseChanged(bool value)
    {
        ApplyFrameToAll();
        _editor.InvalidateScene();
    }

    partial void OnOnionChanged(bool value) => _editor.InvalidateScene();

    /// <summary>Raised when the timelines must redraw.</summary>
    public event Action? TimelineChanged;

    private void RaiseTimeline()
    {
        OnPropertyChanged(nameof(MaxEnd));
        OnPropertyChanged(nameof(FrameText));
        OnPropertyChanged(nameof(ActiveKey));
        OnPropertyChanged(nameof(HasActiveKey));
        OnPropertyChanged(nameof(ShowEaseCombo));
        OnPropertyChanged(nameof(ShowSupport));
        OnPropertyChanged(nameof(ActiveEase));
        OnPropertyChanged(nameof(SupportX));
        OnPropertyChanged(nameof(SupportY));
        TimelineChanged?.Invoke();
        foreach (var t in Tracks)
            t.RaiseKeysChanged();
    }

    private void KeysChanged(DynamicTrack track)
    {
        track.Sort();
        ApplyFrameToAll();
        RaiseTimeline();
    }

    // ================= POSES ================= //

    private static DynamicKey Capture(SceneElement e, int frame)
    {
        var key = new DynamicKey
        {
            Frame = frame,
            X = e.X,
            Y = e.Y,
            Width = e.Width,
            Height = e.Height,
            Rotation = e is ImageElement image ? image.Rotation : 0,
            Color = e is ImageElement img ? img.Color : Colors.White,
            SupportX = 0.5,
            SupportY = 0.5,
            Ease = EasePreset.Linear,
        };
        return key;
    }

    private void Apply(SceneElement e, DynamicKey pose)
    {
        using (_editor.SuppressTracking())
        {
            e.X = pose.X;
            e.Y = pose.Y;
            if (e.HasSize)
            {
                if (e is ImageElement { AutoSize: true } auto && Math.Abs(auto.Width - pose.Width) < 1e-9 && Math.Abs(auto.Height - pose.Height) < 1e-9)
                {
                    // unchanged: keep it auto-sized
                }
                else if (Math.Abs(e.Width - pose.Width) > 1e-9 || Math.Abs(e.Height - pose.Height) > 1e-9)
                {
                    e.Width = pose.Width;
                    e.Height = pose.Height;
                }
            }
            if (e is ImageElement image)
            {
                image.Rotation = pose.Rotation;
                image.Color = pose.Color;
            }
        }
    }

    private static double Bez(double t, double p0, double p1, double p2)
    {
        double u = 1 - t;
        return u * u * p0 + 2 * u * t * p1 + t * t * p2;
    }

    private static double LerpAngle(double a, double b, double t)
    {
        double d = ((b - a) % 360 + 540) % 360 - 180;
        return a + d * t;
    }

    private static Color Lerp(Color a, Color b, double t) => Color.FromArgb(
        (byte)Math.Round(a.A + (b.A - a.A) * t), (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

    /// <summary>Pose of a track at a frame (quadratic Bezier on position, linear size/color, shortest-angle rotation).</summary>
    public DynamicKey Evaluate(DynamicTrack track, int frame)
    {
        var k = track.Keys;
        if (k.Count == 0) return track.Original.Clone();
        if (k.Count == 1 || frame <= k[0].Frame) return k[0].Clone();
        if (frame >= k[^1].Frame) return k[^1].Clone();

        int i = 0;
        for (; i < k.Count - 1; i++)
        {
            if (frame >= k[i].Frame && frame <= k[i + 1].Frame)
                break;
        }
        var a = k[i];
        var b = k[i + 1];
        double t = (frame - a.Frame) / (double)Math.Max(1, b.Frame - a.Frame);
        var (cx, cy) = ControlPoint(a, b);

        var pose = b.Clone();
        pose.Frame = frame;
        pose.X = Bez(t, a.X, cx, b.X);
        pose.Y = Bez(t, a.Y, cy, b.Y);
        pose.Width = a.Width + (b.Width - a.Width) * t;
        pose.Height = a.Height + (b.Height - a.Height) * t;
        pose.Rotation = LerpAngle(a.Rotation, b.Rotation, t);
        pose.Color = Lerp(a.Color, b.Color, t);
        return pose;
    }

    /// <summary>Absolute Bezier control point between two keys.</summary>
    public (double X, double Y) ControlPoint(DynamicKey a, DynamicKey b)
    {
        if (CustomEase || b.Ease == EasePreset.Custom)
            return (a.X + b.SupportX, a.Y + b.SupportY);
        double s = DynamicKey.PresetValue(b.Ease);
        return (a.X + (b.X - a.X) * s, a.Y + (b.Y - a.Y) * s);
    }

    private void ApplyFrameToAll()
    {
        foreach (var track in Tracks)
        {
            if (track.Keys.Count > 0)
                Apply(track.Element, Evaluate(track, TrackFrame(track)));
        }
        _editor.InvalidateScene();
    }

    // ================= BINDING ================= //

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

    private static void RefreshNames(DynamicTrack track)
    {
        track.TransformationNames.Clear();
        foreach (var name in track.Element.Dynamics.Select(d => string.IsNullOrEmpty(d.Name) ? "Transform" : d.Name).Distinct())
            track.TransformationNames.Add(name);
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

    // ================= PLAYBACK ================= //

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

    [RelayCommand]
    private void Restart()
    {
        StopPreview();
        Frame = 0;
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
        track.SelectedFrames.Clear();
        int at = TrackFrame(track);
        foreach (var (df, k) in Clipboard)
        {
            var key = k.Clone();
            key.Frame = Math.Clamp(at + df, 0, track.TotalFrames);
            Upsert(track, key);
            track.SelectedFrames.Add(key.Frame);
        }
        KeysChanged(track);
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
        RaiseTimeline();
    }

    // ================= TRANSFORMATIONS (load / save) ================= //

    private static string N(string? name) => string.IsNullOrEmpty(name) ? "Transform" : name;

    public void SelectTransformation(DynamicTrack track, string name)
    {
        ActiveTrack = track;
        track.TransformationName = name;
        LoadTransformation(track, name);
        KeysChanged(track);
    }

    /// <summary>Unity LoadFromDynamicTransform: rebuilds keys from a saved transformation, starting from the rest pose.</summary>
    private void LoadTransformation(DynamicTrack track, string name)
    {
        track.Keys.Clear();
        track.SelectedFrames.Clear();
        var rest = track.Original.Clone();
        rest.Frame = 0;
        track.Keys.Add(rest);

        var source = track.Element.Dynamics.FirstOrDefault(d => N(d.Name) == name);
        if (source == null)
            return;

        int mc = source.Moves.Count, sc = source.Sizes.Count, rc = source.Rotations.Count, cc = source.Colors.Count;
        int n = Math.Max(Math.Max(mc, sc), Math.Max(rc, cc));
        var cur = rest.Clone();
        int frame = 0;
        for (int i = 0; i < n; i++)
        {
            var m = i < mc ? source.Moves[i] : null;
            var s = i < sc ? source.Sizes[i] : null;
            var r = i < rc ? source.Rotations[i] : null;
            var c = i < cc ? source.Colors[i] : null;

            if (m != null && (int)Math.Round(m.Delay) is int delay && delay > 0)
            {
                // A delay becomes a key with no movement.
                frame += delay;
                var hold = cur.Clone();
                hold.Frame = frame;
                hold.SupportX = hold.SupportY = 0.5;
                hold.Ease = EasePreset.Linear;
                track.Keys.Add(hold);
            }

            int df = 0;
            if (m != null) df = Math.Max(df, m.Frames);
            if (s != null) df = Math.Max(df, s.Frames);
            if (r != null) df = Math.Max(df, r.Frames);
            if (c != null) df = Math.Max(df, c.Frames);

            double mx = m?.FinishX ?? 0, my = m?.FinishY ?? 0;
            double sx = m?.SupportX ?? 0, sy = m?.SupportY ?? 0;
            var next = cur.Clone();
            if (m != null) { next.X += mx; next.Y += my; }
            if (r != null) next.Rotation = cur.Rotation + r.Angle;
            if (c != null) next.Color = c.Finish;
            if (s != null) { next.Width = s.FinalWidth; next.Height = s.FinalHeight; }

            if (CustomEase)
            {
                next.SupportX = sx;
                next.SupportY = sy;
                next.Ease = EasePreset.Custom;
            }
            else if (MatchPreset(mx, my, sx, sy, 0)) SetPreset(next, EasePreset.EaseIn);
            else if (MatchPreset(mx, my, sx, sy, 0.5)) SetPreset(next, EasePreset.Linear);
            else if (MatchPreset(mx, my, sx, sy, 1)) SetPreset(next, EasePreset.EaseOut);
            else
            {
                next.SupportX = sx;
                next.SupportY = sy;
                next.Ease = EasePreset.Custom;
            }

            frame += df;
            next.Frame = frame;
            track.Keys.Add(next);
            cur = next;
        }
        track.TotalFrames = Math.Max(Math.Max(1, frame + 1), track.TotalFrames);
        track.Sort();
    }

    private static void SetPreset(DynamicKey key, EasePreset ease)
    {
        key.Ease = ease;
        key.SupportX = key.SupportY = DynamicKey.PresetValue(ease);
    }

    private static bool MatchPreset(double mx, double my, double sx, double sy, double k)
    {
        bool okx = Math.Abs(mx) <= Eps ? Math.Abs(sx) <= Eps : Math.Abs(sx - mx * k) <= Eps;
        bool oky = Math.Abs(my) <= Eps ? Math.Abs(sy) <= Eps : Math.Abs(sy - my * k) <= Eps;
        return okx && oky;
    }

    /// <summary>Unity BakeToDynamicTransform: turns the keys into Move / Size / Rotation / Color intervals.</summary>
    public DynamicTransformation Bake(DynamicTrack track, string name)
    {
        var t = new DynamicTransformation { Name = name };
        track.Sort();
        var keys = track.Keys;
        if (keys.Count < 2)
            return t;

        var e = track.Element;
        var k0 = keys[0];
        bool anyMove = false, anySize = false, anyRot = false, anyColor = false;
        foreach (var k in keys.Skip(1))
        {
            anyMove |= Math.Abs(k.X - k0.X) > 1e-6 || Math.Abs(k.Y - k0.Y) > 1e-6;
            anySize |= e.HasSize && (Math.Abs(k.Width - k0.Width) > Eps || Math.Abs(k.Height - k0.Height) > Eps);
            anyRot |= Math.Abs(k.Rotation - k0.Rotation) > Eps;
            anyColor |= k.Color != k0.Color;
        }

        double pendingDelay = 0;
        for (int i = 0; i < keys.Count - 1; i++)
        {
            var a = keys[i];
            var b = keys[i + 1];
            int df = Math.Max(0, b.Frame - a.Frame);
            if (df <= 0) continue;

            if (anyMove)
            {
                double dx = b.X - a.X, dy = b.Y - a.Y;
                if (Math.Abs(dx) <= 1e-6 && Math.Abs(dy) <= 1e-6)
                {
                    pendingDelay += df;
                }
                else
                {
                    double supX, supY;
                    if (CustomEase || b.Ease == EasePreset.Custom)
                    {
                        supX = b.SupportX;
                        supY = b.SupportY;
                    }
                    else
                    {
                        double s = DynamicKey.PresetValue(b.Ease);
                        supX = dx * s;
                        supY = dy * s;
                    }
                    t.Moves.Add(new DynamicMove { Frames = df, Delay = pendingDelay, FinishX = Round(dx), FinishY = Round(dy), SupportX = Round(supX), SupportY = Round(supY) });
                    pendingDelay = 0;
                }
            }

            if (anySize)
            {
                var last = t.Sizes.LastOrDefault();
                if (last != null && Math.Abs(last.FinalWidth - b.Width) <= Eps && Math.Abs(last.FinalHeight - b.Height) <= Eps)
                    last.Frames += df;
                else
                    t.Sizes.Add(new DynamicSize { Frames = df, FinalWidth = Round(b.Width), FinalHeight = Round(b.Height) });
            }

            if (anyRot)
            {
                double delta = ((b.Rotation - a.Rotation) % 360 + 540) % 360 - 180;
                var last = t.Rotations.LastOrDefault();
                if (last != null && Math.Abs(last.Angle - delta) <= Eps && last.AnchorX == 0 && last.AnchorY == 0)
                    last.Frames += df;
                else
                    t.Rotations.Add(new DynamicRotation { Angle = Round(delta), Frames = df });
            }

            if (anyColor)
            {
                var last = t.Colors.LastOrDefault();
                if (last != null && last.Start == a.Color && last.Finish == b.Color)
                    last.Frames += df;
                else
                    t.Colors.Add(new DynamicColor { Start = a.Color, Finish = b.Color, Frames = df });
            }
        }
        return t;
    }

    private static double Round(double v) => Math.Round(v, 4);

    /// <summary>Unity "Save": bakes the timeline into the element's transformation with the current name.</summary>
    [RelayCommand]
    private void SaveTransformation(DynamicTrack? track)
    {
        track ??= ActiveTrack;
        if (track == null) return;
        string name = N(track.TransformationName);
        var baked = Bake(track, name);
        var list = track.Element.Dynamics;
        int index = list.FindIndex(d => N(d.Name) == name);
        if (index >= 0) list[index] = baked;
        else list.Add(baked);
        RefreshNames(track);
        _editor.MarkChanged();
        _editor.StatusText = Loc.F("Saved transformation '{0}' on {1}", name, track.Header);
        RaiseTimeline();
    }

    [RelayCommand]
    private void NewTransformation(DynamicTrack? track)
    {
        track ??= ActiveTrack;
        if (track == null) return;
        string name = UniqueName(track, "NewTransform", null);
        track.Element.Dynamics.Add(new DynamicTransformation { Name = name });
        RefreshNames(track);
        track.TransformationName = name;
        LoadTransformation(track, name);
        _editor.MarkChanged();
        KeysChanged(track);
    }

    [RelayCommand]
    private void RemoveTransformation(DynamicTrack? track)
    {
        track ??= ActiveTrack;
        if (track == null) return;
        string name = N(track.TransformationName);
        if (track.Element.Dynamics.RemoveAll(d => N(d.Name) == name) > 0)
            _editor.MarkChanged();
        RefreshNames(track);
        track.TransformationName = track.TransformationNames.FirstOrDefault() ?? "Transform";
        LoadTransformation(track, track.TransformationName);
        KeysChanged(track);
    }

    public void RenameTransformation(DynamicTrack track, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;
        string current = N(track.TransformationName);
        string unique = UniqueName(track, newName.Trim(), current);
        var existing = track.Element.Dynamics.FirstOrDefault(d => N(d.Name) == current);
        if (existing != null)
        {
            existing.Name = unique;
            _editor.MarkChanged();
        }
        track.TransformationName = unique;
        RefreshNames(track);
        RaiseTimeline();
    }

    private static string UniqueName(DynamicTrack track, string requested, string? exclude)
    {
        var names = track.Element.Dynamics.Select(d => N(d.Name)).Where(n => n != exclude).ToHashSet();
        if (!names.Contains(requested)) return requested;
        string candidate = requested + "_copy";
        if (!names.Contains(candidate)) return candidate;
        int i = 1;
        while (names.Contains($"{requested}_copy_{i}")) i++;
        return $"{requested}_copy_{i}";
    }

    // ================= SCENE OVERLAY DATA ================= //

    /// <summary>Path of the element's pivot over the whole timeline (drawn in the Scene).</summary>
    public List<Point> SamplePath(DynamicTrack track, int step = 2)
    {
        var points = new List<Point>();
        if (track.Keys.Count < 2) return points;
        int end = Math.Max(track.Keys[^1].Frame, 1);
        for (int f = 0; f <= end; f += step)
        {
            var p = Evaluate(track, f);
            points.Add(new Point(p.X, p.Y));
        }
        var last = Evaluate(track, end);
        points.Add(new Point(last.X, last.Y));
        return points;
    }

    /// <summary>Previous and next keys around the playhead (onion skin).</summary>
    public IEnumerable<DynamicKey> OnionKeys(DynamicTrack track)
    {
        int f = TrackFrame(track);
        var prev = track.Keys.Where(k => k.Frame < f).OrderByDescending(k => k.Frame).FirstOrDefault();
        var next = track.Keys.Where(k => k.Frame > f).OrderBy(k => k.Frame).FirstOrDefault();
        if (prev != null) yield return prev;
        if (next != null) yield return next;
    }

    /// <summary>The segment the playhead is in, for the custom-ease support handle.</summary>
    public (DynamicKey A, DynamicKey B)? CurrentSegment(DynamicTrack track)
    {
        int f = TrackFrame(track);
        var keys = track.Keys;
        for (int i = 0; i < keys.Count - 1; i++)
        {
            if (f > keys[i].Frame && f <= keys[i + 1].Frame)
                return (keys[i], keys[i + 1]);
        }
        if (keys.Count >= 2 && f == keys[0].Frame)
            return (keys[0], keys[1]);
        return null;
    }

    /// <summary>Scene drag of the support handle: sets the segment end key's support (custom ease).</summary>
    public void SetSupport(DynamicTrack track, DynamicKey a, DynamicKey b, Point absolute)
    {
        b.SupportX = absolute.X - a.X;
        b.SupportY = absolute.Y - a.Y;
        b.Ease = EasePreset.Custom;
        ApplyFrameToAll();
        RaiseTimeline();
    }

    /// <summary>
    /// Adds a trigger at the hierarchy level of the edited elements: when a model enters it, it plays the
    /// transformation of each edited element (one Play Transformation per distinct name).
    /// </summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task AddTriggerAsync()
    {
        if (Tracks.Count == 0)
            return;
        var first = Tracks[0].Element;
        var parent = first.Parent;
        // Elements at the same level as the first one decide the content; the trigger goes right above them.
        var level = Tracks.Where(t => ReferenceEquals(t.Element.Parent, parent)).ToList();
        var siblings = _editor.Document.SiblingsOf(first);
        int index = level.Select(t => siblings.IndexOf(t.Element)).Where(i => i >= 0).DefaultIfEmpty(-1).Min();

        var names = level.Select(t => t.TransformationName?.Trim() ?? string.Empty)
            .Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        // Several transformations: run them one after another, at the same time, or a random few.
        string order = "Straight";
        int randomCount = 1;
        if (level.Count > 1 && names.Count > 1)
        {
            var dialog = new ViewModels.Dialogs.MessageDialogViewModel(Loc.T("Add Trigger"),
                Loc.F("How should the {0} transformations run when the trigger fires?", names.Count), MessageKind.Question);
            dialog.Buttons.Add(new ViewModels.Dialogs.DialogButton(Loc.T("Run After Another"), "Straight", isDefault: true));
            dialog.Buttons.Add(new ViewModels.Dialogs.DialogButton(Loc.T("Run at the Same Time"), "Sync"));
            dialog.Buttons.Add(new ViewModels.Dialogs.DialogButton(Loc.T("Run Randomly"), "Random"));
            dialog.Buttons.Add(new ViewModels.Dialogs.DialogButton(Loc.T("Cancel"), null, isCancel: true));
            if (await AppServices.Dialogs.ShowAsync(dialog) is not string picked)
                return;
            order = picked;
            if (order == "Random")
            {
                int available = names.Count;
                string? text = await AppServices.Dialogs.PromptAsync(Loc.T("Run Randomly"),
                    Loc.F("How many of the {0} transformations should run (picked at random)?", available), "1",
                    value => ValidateRandomCount(value, available), Loc.T("Add Trigger"));
                if (text == null)
                    return;
                randomCount = int.Parse(text.Trim(), System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        string indent = order == "Straight" ? "      " : "        ";
        var transforms = string.Concat(names.Select(n => $"\n{indent}<Transform Name=\"{System.Security.SecurityElement.Escape(n)}\"/>"));
        var actions = order switch
        {
            // Run Order Group (Choose): Sync runs them all together, Random runs Set of them.
            "Sync" => "\n      <Choose Order=\"Sync\">" + transforms + "\n      </Choose>",
            "Random" => $"\n      <Choose Order=\"Random\" Set=\"{randomCount}\">" + transforms + "\n      </Choose>",
            _ => transforms,
        };
        string content =
            "\n  <Init>" +
            "\n    <SetVariable Name=\"$Active\" Value=\"1\"/>" +
            "\n    <SetVariable Name=\"$AI\" Value=\"0\"/>" +
            "\n    <SetVariable Name=\"$Node\" Value=\"COM\"/>" +
            "\n  </Init>" +
            "\n  <Loop>" +
            "\n    <Events>" +
            "\n      <EventBlock Template=\"FreqUsed.Enter\"/>" +
            "\n    </Events>" +
            "\n    <Actions>" +
            "\n      <ActionBlock Template=\"FreqUsed.SwitchOff\"/>" + actions +
            "\n    </Actions>" +
            "\n  </Loop>\n";

        var at = first.WorldPosition;
        var created = await _editor.CreateElementAsync(ElementType.Trigger, new Point(at.X, at.Y), null, null, parent, index);
        if (created is not TriggerElement trigger)
            return;
        trigger.Name = "TriggerTransform";
        trigger.ContentXml = content;
        _editor.StatusText = names.Count == 0
            ? Loc.T("Added a trigger (no transformation name to play).")
            : Loc.F("Added a trigger that plays {0}. Save the transformations so the names exist.", string.Join(", ", names));
    }

    /// <summary>Run Randomly: a whole number from 1 to the number of transformations.</summary>
    private static string? ValidateRandomCount(string value, int available)
    {
        if (!int.TryParse(value.Trim(), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int count))
            return Loc.T("Enter a whole number (for example 1).");
        if (count < 0)
            return Loc.T("Warning: the number can't be negative.");
        if (count == 0)
            return Loc.T("Warning: at least 1 transformation has to run.");
        if (count > available)
            return Loc.F("Error: there are only {0} transformations.", available);
        return null;
    }
}
