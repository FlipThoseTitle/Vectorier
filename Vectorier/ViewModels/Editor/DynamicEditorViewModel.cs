using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Vectorier.ViewModels.Editor;

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

    /// <summary>Auto Key: a Scene / Inspector change of an edited element keys its pose at the playhead once the input is finished.</summary>
    [ObservableProperty] private bool _autoKey;

    /// <summary>Shows the motion paths (and their key dots) in the Scene.</summary>
    [ObservableProperty] private bool _showPath = true;

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

    partial void OnShowPathChanged(bool value) => _editor.InvalidateScene();

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

    /// <summary>
    /// After a change of a track's keys: redraws, previews and records an undo step. <paramref name="edit"/> marks the
    /// timeline as not saved into its transformation yet.
    /// </summary>
    private void KeysChanged(DynamicTrack track, bool edit = true)
    {
        track.Sort();
        if (edit)
            track.IsModified = true;
        ApplyFrameToAll();
        RaiseTimeline();
        if (edit)
        {
            Share(track);
            _editor.MarkChanged();
        }
    }

    /// <summary>A continuous edit (dragging keys or the support handle) is a single undo step.</summary>
    public void BeginEdit() => _editor.BeginGesture();

    public void EndEdit() => _editor.EndGesture();
}
