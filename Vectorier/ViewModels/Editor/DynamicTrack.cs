using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Vectorier.Models.Scene;

namespace Vectorier.ViewModels.Editor;

/// <summary>Kind of Scene transform being made (Auto Key skips components the element cannot animate).</summary>
public enum DynamicTransformKind
{
    None,
    Rotate,
    Scale,
}

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

    /// <summary>The timeline has changes that are not baked into a transformation yet.</summary>
    [ObservableProperty] private bool _isModified;

    public ObservableCollection<string> TransformationNames { get; } = new();

    /// <summary>Re-sends the picked name (the transformation list was rebuilt and the combo box lost its pick).</summary>
    public void NotifyTransformationName() => OnPropertyChanged(nameof(TransformationName));

    /// <summary>Pose before the editor touched the element; put back when it is unbound.</summary>
    public DynamicKey Original { get; set; } = new();

    public string Header => Element.DisplayName;

    /// <summary>Raised when keys change (the timeline redraws).</summary>
    public event Action? KeysChanged;

    public void RaiseKeysChanged() => KeysChanged?.Invoke();

    public void Sort() => Keys.Sort((a, b) => a.Frame.CompareTo(b.Frame));

    public DynamicKey? KeyAt(int frame) => Keys.FirstOrDefault(k => k.Frame == frame);
}
