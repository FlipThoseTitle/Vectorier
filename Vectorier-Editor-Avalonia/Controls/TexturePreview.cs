using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Vectorier_Editor_Avalonia.Services.Assets;

namespace Vectorier_Editor_Avalonia.Controls;

/// <summary>Shared 20 FPS clock for animated previews. Runs only while something listens.</summary>
public static class AnimationClock
{
    private static readonly Stopwatch Watch = Stopwatch.StartNew();
    private static DispatcherTimer? _timer;
    private static int _subscribers;
    private static event Action? TickInternal;

    public static double Seconds => Watch.Elapsed.TotalSeconds;

    public static event Action? Tick
    {
        add
        {
            TickInternal += value;
            _subscribers++;
            if (_timer == null)
            {
                _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(1000.0 / TextureAsset.FramesPerSecond), DispatcherPriority.Background, (_, _) => TickInternal?.Invoke());
            }
            _timer.Start();
        }
        remove
        {
            TickInternal -= value;
            _subscribers = Math.Max(0, _subscribers - 1);
            if (_subscribers == 0)
                _timer?.Stop();
        }
    }
}

/// <summary>
/// Shows a project texture by name, fitted inside the control. Sprite sheets (.plist) are animated
/// instead of showing the whole sheet. Loading happens only while the control is on screen.
/// </summary>
public class TexturePreview : Control
{
    public static readonly StyledProperty<TextureLibrary?> LibraryProperty =
        AvaloniaProperty.Register<TexturePreview, TextureLibrary?>(nameof(Library));

    public static readonly StyledProperty<string?> TextureNameProperty =
        AvaloniaProperty.Register<TexturePreview, string?>(nameof(TextureName));

    /// <summary>A specific file (when a static texture and a sprite sheet share a name). Wins over <see cref="TextureName"/>.</summary>
    public static readonly StyledProperty<TextureEntry?> EntryProperty =
        AvaloniaProperty.Register<TexturePreview, TextureEntry?>(nameof(Entry));

    public static readonly StyledProperty<bool> UseThumbnailProperty =
        AvaloniaProperty.Register<TexturePreview, bool>(nameof(UseThumbnail), true);

    public static readonly StyledProperty<IBrush?> TintProperty =
        AvaloniaProperty.Register<TexturePreview, IBrush?>(nameof(Tint));

    private TextureAsset? _asset;
    private Bitmap? _thumbnail;
    private bool _attached;
    private bool _animating;
    private int _requestVersion;

    static TexturePreview()
    {
        AffectsRender<TexturePreview>(TintProperty);
    }

    public TextureLibrary? Library
    {
        get => GetValue(LibraryProperty);
        set => SetValue(LibraryProperty, value);
    }

    public string? TextureName
    {
        get => GetValue(TextureNameProperty);
        set => SetValue(TextureNameProperty, value);
    }

    public TextureEntry? Entry
    {
        get => GetValue(EntryProperty);
        set => SetValue(EntryProperty, value);
    }

    /// <summary>Static textures use a downscaled bitmap (grids). Turn off for large previews.</summary>
    public bool UseThumbnail
    {
        get => GetValue(UseThumbnailProperty);
        set => SetValue(UseThumbnailProperty, value);
    }

    public IBrush? Tint
    {
        get => GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LibraryProperty || change.Property == TextureNameProperty || change.Property == UseThumbnailProperty ||
            change.Property == EntryProperty)
            Reload();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Reload();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        StopAnimation();
    }

    private async void Reload()
    {
        int version = ++_requestVersion;
        _asset = null;
        _thumbnail = null;
        StopAnimation();
        InvalidateVisual();

        var library = Library;
        var entry = Entry;
        string? name = entry?.Name ?? TextureName;
        if (!_attached || library == null || string.IsNullOrEmpty(name))
            return;

        entry ??= library.Find(name);
        if (entry == null)
            return;

        if (entry.IsAnimated || !UseThumbnail)
        {
            // A static file that shares its name with a sprite sheet is loaded on its own.
            var asset = ReferenceEquals(library.Find(name), entry) ? await library.GetAsync(name) : await library.LoadEntryAsync(entry);
            if (version != _requestVersion)
                return;
            _asset = asset;
            if (asset?.IsAnimated == true && _attached)
                StartAnimation();
        }
        else
        {
            var thumb = await library.GetThumbnailAsync(entry, 160);
            if (version != _requestVersion)
                return;
            _thumbnail = thumb;
        }
        InvalidateVisual();
    }

    private void StartAnimation()
    {
        if (_animating)
            return;
        _animating = true;
        AnimationClock.Tick += OnTick;
    }

    private void StopAnimation()
    {
        if (!_animating)
            return;
        _animating = false;
        AnimationClock.Tick -= OnTick;
    }

    private void OnTick() => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        double w, h;
        if (_asset != null)
        {
            w = _asset.Width;
            h = _asset.Height;
        }
        else if (_thumbnail != null)
        {
            w = _thumbnail.PixelSize.Width;
            h = _thumbnail.PixelSize.Height;
        }
        else
        {
            return;
        }

        double scale = Math.Min(bounds.Width / Math.Max(1, w), bounds.Height / Math.Max(1, h));
        scale = Math.Min(scale, 4);
        var dest = new Rect((bounds.Width - w * scale) / 2, (bounds.Height - h * scale) / 2, w * scale, h * scale);

        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.MediumQuality }))
        {
            if (_asset != null)
                _asset.Draw(context, dest, _asset.FrameAt(AnimationClock.Seconds));
            else if (_thumbnail != null)
                context.DrawImage(_thumbnail, dest);
        }
    }
}
