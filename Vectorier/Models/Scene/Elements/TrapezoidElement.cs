using System;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Vectorier.Models.Scene;

/// <summary>
/// &lt;Trapezoid&gt;: sloped collision. Stored as a bounding box (X, Y = top-left, Width, Height) plus the
/// height of the rectangular base; the slope uses the fixed angle <see cref="SlopeAngle"/>.
/// Type 1 rises to the right, Type 2 is mirrored.
/// </summary>
public sealed partial class TrapezoidElement : SceneElement
{
    public const double SlopeAngle = 26.5650512;
    public static readonly double SlopeRatio = Math.Tan(SlopeAngle * Math.PI / 180.0); // ~0.5

    public override ElementType Type => ElementType.Trapezoid;
    public override bool IntegerTransform => true;
    public override bool LayerEditable => false;

    [ObservableProperty] private int _trapezoidType = 1;
    [ObservableProperty] private double _baseHeight = 1;

    public double SlopeHeight => Math.Max(0, Height - BaseHeight);

    partial void OnBaseHeightChanged(double value) => OnPropertyChanged(nameof(SlopeHeight));

    /// <summary>Recomputes the height so the slope keeps the fixed angle.</summary>
    public void ApplySlopeAngle()
    {
        Height = Math.Round(BaseHeight + Width * SlopeRatio);
    }

    public override Point[] GetLocalCorners()
    {
        double slope = SlopeHeight;
        if (TrapezoidType == 2)
        {
            return new[]
            {
                new Point(X, Y),
                new Point(X + Width, Y + slope),
                new Point(X + Width, Y + Height),
                new Point(X, Y + Height),
            };
        }
        return new[]
        {
            new Point(X, Y + slope),
            new Point(X + Width, Y),
            new Point(X + Width, Y + Height),
            new Point(X, Y + Height),
        };
    }

    public override Rect GetLocalBounds() => new(X, Y, Width, Height);

    public override void ScaleAround(Point pivot, double sx, double sy)
    {
        double slope = SlopeHeight;
        X = pivot.X + (X - pivot.X) * sx;
        Y = pivot.Y + (Y - pivot.Y) * sy;
        Width *= Math.Abs(sx);
        BaseHeight = Math.Max(1, BaseHeight * Math.Abs(sy));
        Height = BaseHeight + slope * Math.Abs(sy);
    }

    protected override SceneElement CreateEmpty() => new TrapezoidElement();

    protected override void CopyTo(SceneElement target)
    {
        base.CopyTo(target);
        var t = (TrapezoidElement)target;
        t.TrapezoidType = TrapezoidType;
        t.BaseHeight = BaseHeight;
    }
}
