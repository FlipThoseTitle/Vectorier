using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Vectorier_Editor_Avalonia.ViewModels.Preview;

namespace Vectorier_Editor_Avalonia.Controls;

/// <summary>Draws the character preview's silhouette: the camera point is the middle of the view.</summary>
public sealed class CharacterPreviewCanvas : Control
{
    public static readonly StyledProperty<CharacterPreviewViewModel?> PreviewProperty =
        AvaloniaProperty.Register<CharacterPreviewCanvas, CharacterPreviewViewModel?>(nameof(Preview));

    private static readonly IPen AxisPen = new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80)), 1);

    private double _zoom = 1;

    public CharacterPreviewCanvas()
    {
        Focusable = true;
    }

    /// <summary>Mouse wheel zooms the view (around its center).</summary>
    protected override void OnPointerWheelChanged(Avalonia.Input.PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _zoom = Math.Clamp(_zoom * Math.Pow(1.15, e.Delta.Y), 0.1, 20);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
    }

    public CharacterPreviewViewModel? Preview
    {
        get => GetValue(PreviewProperty);
        set => SetValue(PreviewProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PreviewProperty)
        {
            if (change.OldValue is CharacterPreviewViewModel old) old.Redraw -= InvalidateVisual;
            if (change.NewValue is CharacterPreviewViewModel vm) vm.Redraw += InvalidateVisual;
            InvalidateVisual();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (Preview is { } vm) vm.Redraw -= InvalidateVisual;
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        context.FillRectangle(Brushes.White, new Rect(size));
        var center = new Point(size.Width / 2, size.Height / 2);
        context.DrawLine(AxisPen, new Point(0, center.Y), new Point(size.Width, center.Y));
        context.DrawLine(AxisPen, new Point(center.X, 0), new Point(center.X, size.Height));
        if (Preview is not { Silhouette: { } geometry } vm)
            return;
        double scale = size.Height * 0.6 / Math.Max(1, vm.ModelHeight) * _zoom;
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(center.X, center.Y)))
            context.DrawGeometry(Brushes.Black, null, geometry);
    }
}
