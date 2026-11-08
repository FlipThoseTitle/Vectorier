using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Vectorier.Services.Assets;

namespace Vectorier.Controls;

/// <summary>
/// Shows a project texture by name, fitted inside the control. Sprite sheets (.plist) are animated
/// instead of showing the whole sheet. Loading happens only while the control is on screen. Static textures are
/// drawn from the library's downscaled copies at the control's pixel size, fetched while rendering and never kept,
/// so thousands of tiles do not keep thousands of bitmaps in memory.
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

    // Full-size textures: one of the library's is looked up by name at render time (the library may let it go and
    // load it again); a file shadowed by a sprite sheet of the same name is loaded for this preview and owned.
    private string? _assetName;
    private TextureAsset? _owned;
    private TextureEntry? _thumbEntry;
    private TextureLibrary? _subscribed;
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

    private void Subscribe(TextureLibrary? library)
    {
        if (ReferenceEquals(_subscribed, library))
            return;
        if (_subscribed != null)
            _subscribed.TextureLoaded -= OnTextureLoaded;
        _subscribed = library;
        if (library != null)
            library.TextureLoaded += OnTextureLoaded;
    }

    private void OnTextureLoaded(string name)
    {
        if ((_thumbEntry != null && name == _thumbEntry.Name) || name == _assetName)
            InvalidateVisual();
    }

    private void DropTexture()
    {
        _assetName = null;
        _owned?.Bitmap.Dispose();
        _owned = null;
        _thumbEntry = null;
    }

    private TextureAsset? CurrentAsset => _owned ?? (_assetName != null ? Library?.TryGet(_assetName) : null);

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        StopAnimation();
        Subscribe(null);
        // A recycled tile must not keep a texture alive.
        DropTexture();
    }

    private async void Reload()
    {
        int version = ++_requestVersion;
        DropTexture();
        StopAnimation();
        Subscribe(null);
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
            bool shared = ReferenceEquals(library.Find(name), entry);
            var asset = shared ? await library.GetAsync(name) : await library.LoadEntryAsync(entry);
            if (version != _requestVersion)
            {
                if (!shared)
                    asset?.Bitmap.Dispose();
                return;
            }
            if (shared)
            {
                _assetName = name;
                Subscribe(library); // drawn again if the library loads it again
            }
            else
            {
                _owned = asset;
            }
            if (asset?.IsAnimated == true && _attached)
                StartAnimation();
        }
        else
        {
            // Drawn from the library's thumbnails at render time; redraw when one arrives.
            _thumbEntry = entry;
            Subscribe(library);
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
        Bitmap? thumbnail = null;
        var asset = CurrentAsset;
        if (asset != null)
        {
            w = asset.Width;
            h = asset.Height;
        }
        else if (_thumbEntry != null && Library is { } library)
        {
            double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            thumbnail = library.TryGetThumbnail(_thumbEntry, (int)Math.Ceiling(Math.Max(bounds.Width, bounds.Height) * scaling), nearest: true);
            if (thumbnail == null)
                return;
            w = thumbnail.PixelSize.Width;
            h = thumbnail.PixelSize.Height;
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
            if (asset != null)
                asset.Draw(context, dest, asset.FrameAt(AnimationClock.Seconds));
            else if (thumbnail != null)
                context.DrawImage(thumbnail, dest);
        }
    }
}
