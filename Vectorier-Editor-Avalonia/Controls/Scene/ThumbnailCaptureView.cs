using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Vectorier_Editor_Avalonia.ViewModels.Editor;

namespace Vectorier_Editor_Avalonia.Controls.Scene;

/// <summary>
/// The thumbnail frame of <see cref="ThumbnailCaptureViewModel"/>: the level seen through a 512 × 340 frame.
/// Drag with the left or right mouse button to pan, wheel to zoom.
/// </summary>
public sealed class ThumbnailCaptureView : Control
{
    public static readonly StyledProperty<ThumbnailCaptureViewModel?> CaptureProperty =
        AvaloniaProperty.Register<ThumbnailCaptureView, ThumbnailCaptureViewModel?>(nameof(Capture));

    private static readonly IBrush Outside = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
    private static readonly IPen FramePen = new Pen(new SolidColorBrush(Color.FromRgb(0x29, 0x79, 0xFF)), 2);

    private SceneRenderer? _renderer;
    private Point? _drag;

    static ThumbnailCaptureView()
    {
        ClipToBoundsProperty.OverrideDefaultValue<ThumbnailCaptureView>(true);
    }

    public ThumbnailCaptureView()
    {
        Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    public ThumbnailCaptureViewModel? Capture
    {
        get => GetValue(CaptureProperty);
        set => SetValue(CaptureProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CaptureProperty)
        {
            if (change.OldValue is ThumbnailCaptureViewModel old)
            {
                old.Changed -= InvalidateVisual;
                old.Editor.Context.Textures.TextureLoaded -= OnTextureLoaded;
            }
            if (change.NewValue is ThumbnailCaptureViewModel vm)
            {
                vm.Changed += InvalidateVisual;
                vm.Editor.Context.Textures.TextureLoaded += OnTextureLoaded;
                _renderer = new SceneRenderer(vm.Editor);
                _renderer.RedrawRequested += () => Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
            }
            else
            {
                _renderer = null;
            }
            InvalidateVisual();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (Capture is { } vm)
        {
            vm.Changed -= InvalidateVisual;
            vm.Editor.Context.Textures.TextureLoaded -= OnTextureLoaded;
        }
    }

    private void OnTextureLoaded(string _) => InvalidateVisual();

    /// <summary>The frame, as large as fits with a margin, centered.</summary>
    private Rect FrameRect()
    {
        var size = Bounds.Size;
        double aspect = ThumbnailCaptureViewModel.Aspect;
        double w = Math.Max(10, size.Width - 40), h = Math.Max(10, size.Height - 40);
        if (w / h > aspect) w = h * aspect;
        else h = w / aspect;
        return new Rect((size.Width - w) / 2, (size.Height - h) / 2, w, h);
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        context.FillRectangle(Outside, new Rect(size));
        if (Capture is not { } vm || _renderer == null)
            return;
        var frame = FrameRect();
        double zoom = frame.Height / vm.FrameHeight;
        using (context.PushClip(frame))
        {
            context.FillRectangle(Brushes.White, frame);
            _renderer.Render(context, new SceneViewport(vm.Center, zoom, size), SceneRenderMode.Thumbnail);
        }
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
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag is not { } last || Capture is not { } vm)
            return;
        var pos = e.GetPosition(this);
        double zoom = FrameRect().Height / vm.FrameHeight;
        vm.Center -= (pos - last) / zoom;
        _drag = pos;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Capture is { } vm)
            vm.FrameHeight /= Math.Pow(1.15, e.Delta.Y);
        e.Handled = true;
    }
}
