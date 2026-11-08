using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Vectorier.Models.Scene;

/// <summary>&lt;Animation&gt;: an animated sprite that can move by itself (Type 1: direction/acceleration).</summary>
public sealed partial class AnimationElement : SceneElement
{
    public override ElementType Type => ElementType.Animation;
    public override bool NameEditable => false;

    [ObservableProperty] private int _animationType = 1;
    [ObservableProperty] private string _direction = "0|0";
    [ObservableProperty] private string _acceleration = "0|0";
    [ObservableProperty] private double _time;

    // The game draws an animation at its XML Width / Height times its ScaleX / ScaleY. In the editor, Width and
    // Height are the size it is drawn at; the file keeps the original size (the texture's, or the file's own)
    // and the scale that gives the size shown (see SceneXmlWriter.WriteAnimation).

    /// <summary>The original size written as Width / Height (0: unknown, the size is written with ScaleX / ScaleY).</summary>
    public double NativeWidth { get; set; }
    public double NativeHeight { get; set; }

    /// <summary>The file's ScaleX / ScaleY, kept only for an animation without an original size.</summary>
    public double ScaleX { get; set; } = 1;
    public double ScaleY { get; set; } = 1;

    /// <summary>Direction as two numbers (game space: X right, Y down, units per frame).</summary>
    public (double X, double Y) DirectionVector
    {
        get => ParsePair(Direction);
        set => Direction = FormatPair(value.X, value.Y);
    }

    /// <summary>Acceleration X (Inspector).</summary>
    public double AccelerationX
    {
        get => ParsePair(Acceleration).X;
        set => Acceleration = FormatPair(value, ParsePair(Acceleration).Y);
    }

    /// <summary>Acceleration Y, up positive like the Inspector's Position (the file's is down positive).</summary>
    public double AccelerationUp
    {
        get => -ParsePair(Acceleration).Y;
        set => Acceleration = FormatPair(ParsePair(Acceleration).X, -value);
    }

    partial void OnAccelerationChanged(string value)
    {
        OnPropertyChanged(nameof(AccelerationX));
        OnPropertyChanged(nameof(AccelerationUp));
    }

    partial void OnDirectionChanged(string value) => OnPropertyChanged(nameof(DirectionVector));

    private static (double X, double Y) ParsePair(string? text)
    {
        var parts = (text ?? string.Empty).Split('|');
        static double D(string s) => double.TryParse(s.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
        return (parts.Length > 0 ? D(parts[0]) : 0, parts.Length > 1 ? D(parts[1]) : 0);
    }

    private static string FormatPair(double x, double y)
    {
        static string F(double v) => Math.Round(v, 4).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        return F(x) + "|" + F(y);
    }

    public string ClassName
    {
        get => Name;
        set => Name = value;
    }

    protected override SceneElement CreateEmpty() => new AnimationElement();

    protected override void CopyTo(SceneElement target)
    {
        base.CopyTo(target);
        var t = (AnimationElement)target;
        t.AnimationType = AnimationType;
        t.Direction = Direction;
        t.Acceleration = Acceleration;
        t.Time = Time;
        t.ScaleX = ScaleX;
        t.ScaleY = ScaleY;
        t.NativeWidth = NativeWidth;
        t.NativeHeight = NativeHeight;
    }
}
