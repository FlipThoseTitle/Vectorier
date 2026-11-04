using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Vectorier.ViewModels.Dialogs;

namespace Vectorier.Controls;

/// <summary>
/// The canvas of <see cref="ImageCropViewModel"/>: the target-resolution frame, as large as fits, with the image
/// in it. What falls outside the frame is shown dimmed (it is cut off when saving). Drag with the left or right
/// mouse button to move the image, wheel to zoom around the pointer (touchpad: two-finger scroll moves, pinch zooms).
/// </summary>
public sealed class ImageCropCanvas : Control
{
    public static readonly StyledProperty<ImageCropViewModel?> CropProperty =
        AvaloniaProperty.Register<ImageCropCanvas, ImageCropViewModel?>(nameof(Crop));

    private static readonly IBrush Outside = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
    private static readonly IBrush Dim = new SolidColorBrush(Color.FromArgb(0xB4, 0x2A, 0x2A, 0x2A));
    private static readonly IPen FramePen = new Pen(new SolidColorBrush(Color.FromRgb(0x29, 0x79, 0xFF)), 2);
    private static IBrush? _checker;

    private Point? _drag;

    static ImageCropCanvas()
    {
        ClipToBoundsProperty.OverrideDefaultValue<ImageCropCanvas>(true);
        FocusableProperty.OverrideDefaultValue<ImageCropCanvas>(true);
    }

    public ImageCropCanvas()
    {
        Cursor = new Cursor(StandardCursorType.SizeAll);
        AddHandler(PointerTouchPadGestureMagnifyEvent, OnTouchPadMagnify);
    }

    public ImageCropViewModel? Crop
    {
        get => GetValue(CropProperty);
        set => SetValue(CropProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CropProperty)
        {
            if (change.OldValue is ImageCropViewModel old)
                old.Changed -= InvalidateVisual;
            if (change.NewValue is ImageCropViewModel vm && VisualRoot != null)
                vm.Changed += InvalidateVisual;
            InvalidateVisual();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Crop is { } vm)
        {
            vm.Changed -= InvalidateVisual;
            vm.Changed += InvalidateVisual;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (Crop is { } vm)
            vm.Changed -= InvalidateVisual;
    }

    /// <summary>The frame on screen (the target resolution scaled to fit with a margin), centered.</summary>
    private Rect FrameRect(ImageCropViewModel vm)
    {
        var size = Bounds.Size;
        double aspect = (double)vm.TargetWidth / vm.TargetHeight;
        double w = Math.Max(10, size.Width - 40), h = Math.Max(10, size.Height - 40);
        if (w / h > aspect) w = h * aspect;
        else h = w / aspect;
        return new Rect((size.Width - w) / 2, (size.Height - h) / 2, w, h);
    }

    /// <summary>Screen pixels per canvas pixel.</summary>
    private double ViewScale(ImageCropViewModel vm) => FrameRect(vm).Width / vm.TargetWidth;

    private Point ToCanvas(ImageCropViewModel vm, Point screen)
    {
        var frame = FrameRect(vm);
        double s = frame.Width / vm.TargetWidth;
        return new Point((screen.X - frame.X) / s, (screen.Y - frame.Y) / s);
    }

    /// <summary>A grey and white checkerboard (transparent areas), made once.</summary>
    private static IBrush Checker()
    {
        if (_checker != null)
            return _checker;
        var tile = new RenderTargetBitmap(new PixelSize(16, 16));
        using (var ctx = tile.CreateDrawingContext(true))
        {
            var light = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            var dark = new SolidColorBrush(Color.FromRgb(0xD6, 0xD6, 0xD6));
            ctx.FillRectangle(light, new Rect(0, 0, 16, 16));
            ctx.FillRectangle(dark, new Rect(0, 0, 8, 8));
            ctx.FillRectangle(dark, new Rect(8, 8, 8, 8));
        }
        _checker = new ImageBrush(tile)
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.None,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
            DestinationRect = new RelativeRect(0, 0, 16, 16, RelativeUnit.Absolute),
        };
        return _checker;
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        context.FillRectangle(Outside, new Rect(size));
        if (Crop is not { } vm)
            return;
        var frame = FrameRect(vm);
        double s = frame.Width / vm.TargetWidth;
        var image = vm.ImageRect;
        var screenImage = new Rect(frame.X + image.X * s, frame.Y + image.Y * s, image.Width * s, image.Height * s);

        // Inside the frame: the background that will be saved, then the image.
        using (context.PushClip(frame))
            context.FillRectangle(vm.BackgroundBrush ?? Checker(), frame);

        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.MediumQuality }))
            context.DrawImage(vm.Source, new Rect(0, 0, vm.SourceWidth, vm.SourceHeight), screenImage);

        // Outside the frame: the parts that are cut off, dimmed.
        var all = new Rect(size);
        context.FillRectangle(Dim, new Rect(all.X, all.Y, all.Width, Math.Max(0, frame.Y - all.Y)));
        context.FillRectangle(Dim, new Rect(all.X, frame.Bottom, all.Width, Math.Max(0, all.Bottom - frame.Bottom)));
        context.FillRectangle(Dim, new Rect(all.X, frame.Y, Math.Max(0, frame.X - all.X), frame.Height));
        context.FillRectangle(Dim, new Rect(frame.Right, frame.Y, Math.Max(0, all.Right - frame.Right), frame.Height));

        context.DrawRectangle(null, FramePen, frame.Inflate(1));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsLeftButtonPressed || props.IsRightButtonPressed)
        {
            _drag = e.GetPosition(this);
            e.Pointer.Capture(this);
            Focus();
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag is not { } last || Crop is not { } vm)
            return;
        var pos = e.GetPosition(this);
        vm.Center += (pos - last) / ViewScale(vm);
        _drag = pos;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _drag = null;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Crop is { } vm)
        {
            if (!TouchpadInput.IsPinch(e) && TouchpadInput.IsTouchpadScroll(e))
                vm.Center += e.Delta * TouchpadInput.PixelsPerUnit / ViewScale(vm);
            else
                vm.ZoomAt(ToCanvas(vm, e.GetPosition(this)), TouchpadInput.WheelZoomFactor(e));
        }
        e.Handled = true;
    }

    private void OnTouchPadMagnify(object? sender, PointerDeltaEventArgs e)
    {
        if (Crop is { } vm)
            vm.ZoomAt(ToCanvas(vm, e.GetPosition(this)), TouchpadInput.MagnifyFactor(e));
        e.Handled = true;
    }

    /// <summary>Arrow keys nudge the image by one saved pixel (Shift: ten).</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Crop is not { } vm)
            return;
        double step = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? 10 : 1;
        Vector? move = e.Key switch
        {
            Key.Left => new Vector(-step, 0),
            Key.Right => new Vector(step, 0),
            Key.Up => new Vector(0, -step),
            Key.Down => new Vector(0, step),
            _ => null,
        };
        if (move is { } m)
        {
            vm.Center += m;
            e.Handled = true;
        }
    }
}
