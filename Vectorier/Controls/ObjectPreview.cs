using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Vectorier.Controls.Scene;
using Vectorier.Models.Scene;
using Vectorier.Services.Assets;

namespace Vectorier.Controls;

/// <summary>
/// Thumbnail of an object definition (prefab): every element it contains, nested references included,
/// fitted inside the control. The picture is rendered once into a bitmap of the tile's size from downscaled
/// textures (sized for the tile, not the full-resolution files), so browsing objects stays light. Spawns show their
/// gizmo and Models their silhouette, as in the Scene.
/// </summary>
public sealed class ObjectPreview : Control
{
    public static readonly StyledProperty<ObjectDefinition?> DefinitionProperty =
        AvaloniaProperty.Register<ObjectPreview, ObjectDefinition?>(nameof(Definition));

    public static readonly StyledProperty<TextureLibrary?> LibraryProperty =
        AvaloniaProperty.Register<ObjectPreview, TextureLibrary?>(nameof(Library));

    public static readonly StyledProperty<ModelLibrary?> ModelsProperty =
        AvaloniaProperty.Register<ObjectPreview, ModelLibrary?>(nameof(Models));

    private static readonly IBrush TriggerFill = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0x00));
    private static readonly IBrush AreaFill = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0x00, 0x00));
    private static readonly IBrush CollisionFill = new SolidColorBrush(Color.FromArgb(0x60, 0x00, 0x00, 0xFF));
    private static readonly IBrush OtherFill = new SolidColorBrush(Color.FromArgb(0x80, 0x40, 0x40, 0x40));

    private readonly List<(SceneElement Element, Vector Offset)> _leaves = new();
    private Rect _bounds;
    private bool _attached;

    static ObjectPreview()
    {
        AffectsRender<ObjectPreview>(DefinitionProperty);
        ClipToBoundsProperty.OverrideDefaultValue<ObjectPreview>(true);
    }

    public ObjectDefinition? Definition
    {
        get => GetValue(DefinitionProperty);
        set => SetValue(DefinitionProperty, value);
    }

    public TextureLibrary? Library
    {
        get => GetValue(LibraryProperty);
        set => SetValue(LibraryProperty, value);
    }

    /// <summary>Where the silhouettes of Model elements come from.</summary>
    public ModelLibrary? Models
    {
        get => GetValue(ModelsProperty);
        set => SetValue(ModelsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DefinitionProperty)
            Collect();
        else if (change.Property == ModelsProperty)
        {
            _version++;
            InvalidateVisual();
        }
        else if (change.Property == LibraryProperty)
        {
            if (change.OldValue is TextureLibrary old) old.TextureLoaded -= OnTextureLoaded;
            if (_attached && change.NewValue is TextureLibrary lib) lib.TextureLoaded += OnTextureLoaded;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        if (Library != null)
            Library.TextureLoaded += OnTextureLoaded;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        if (Library != null)
            Library.TextureLoaded -= OnTextureLoaded;
        // Off-screen tiles (virtualized rows) don't keep their bitmap.
        _cache?.Dispose();
        _cache = null;
    }

    private RenderTargetBitmap? _cache;
    private bool _cacheComplete;
    private int _version;
    private int _cacheVersion = -1;

    private void OnTextureLoaded(string name)
    {
        // Only textures this preview draws matter (a project load fires this for every texture).
        if (!_names.Contains(name))
            return;
        _version++;
        InvalidateVisual();
    }

    private readonly HashSet<string> _names = new(StringComparer.Ordinal);

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        _version++;
    }

    private void Collect()
    {
        _leaves.Clear();
        var def = Definition;
        if (def == null)
            return;
        foreach (var element in def.Content)
            Visit(element, default, 0);
        _names.Clear();
        foreach (var (e, _) in _leaves)
        {
            if (e is ImageElement image) _names.Add(image.ClassName);
            else if (e is AnimationElement or ParticleElement) _names.Add(e.Name);
        }
        UpdateBounds();
        _version++;
    }

    private void Visit(SceneElement element, Vector offset, int depth)
    {
        if (depth > 24)
            return;
        if (element is ObjectElement obj)
        {
            var inner = offset + new Vector(obj.X, obj.Y);
            if (obj.Reference != null)
                foreach (var child in obj.Reference.Content)
                    Visit(child, inner, depth + 1);
            foreach (var child in obj.Children)
                Visit(child, inner, depth + 1);
            return;
        }
        if (element is CommentElement or UnknownElement)
            return;
        _leaves.Add((element, offset));
    }

    private void UpdateBounds()
    {
        Rect? union = null;
        foreach (var (e, o) in _leaves)
        {
            var b = e.GetLocalBounds().Translate(o);
            if (b.Width <= 0 && b.Height <= 0)
                b = b.Inflate(20);
            union = union == null ? b : union.Value.Union(b);
        }
        _bounds = union ?? default;
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (_leaves.Count == 0 || size.Width <= 0 || size.Height <= 0)
            return;

        if (_cache == null || _cacheVersion != _version)
            BuildCache(size);
        if (_cache != null)
            context.DrawImage(_cache, new Rect(0, 0, _cache.PixelSize.Width, _cache.PixelSize.Height), new Rect(size));
    }

    private void BuildCache(Size size)
    {
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var pixels = new PixelSize(Math.Max(1, (int)Math.Ceiling(size.Width * scaling)), Math.Max(1, (int)Math.Ceiling(size.Height * scaling)));
        if (_cache == null || _cache.PixelSize != pixels)
        {
            _cache?.Dispose();
            _cache = new RenderTargetBitmap(pixels, new Vector(96 * scaling, 96 * scaling));
        }
        _cacheVersion = _version;
        using var context = _cache.CreateDrawingContext(true);
        _cacheComplete = Draw(context, size, scaling);
    }

    /// <summary>Draws every leaf; returns false while some texture is still loading.</summary>
    private bool Draw(DrawingContext context, Size size, double scaling)
    {
        var library = Library;
        bool complete = true;

        // Auto-sized images get their size once the texture is known.
        bool resized = false;
        foreach (var (e, _) in _leaves)
        {
            if (e is ImageElement { AutoSize: true } image && library?.Find(image.ClassName) is { } entry)
            {
                var (w, h) = NaturalSize(library, entry);
                if (w > 0 && (Math.Abs(image.Width - w) > 1e-6 || Math.Abs(image.Height - h) > 1e-6))
                {
                    image.FillNaturalSize(w, h);
                    resized = true;
                }
            }
        }
        // Models take the size of their silhouette once it is loaded.
        foreach (var (e, _) in _leaves)
        {
            if (e is ModelElement model && Silhouette(model, ref complete) is { } silhouette && model.SilhouetteBounds != silhouette.Bounds)
            {
                model.SilhouetteBounds = silhouette.Bounds;
                resized = true;
            }
        }
        if (resized)
        {
            UpdateBounds();
            Definition?.ComputeBounds();
        }

        var b = _bounds;
        if (b.Width <= 0 || b.Height <= 0)
            return complete;
        double scale = Math.Min((size.Width - 4) / b.Width, (size.Height - 4) / b.Height);
        var fit = Matrix.CreateTranslation(-b.X, -b.Y) * Matrix.CreateScale(scale, scale) *
                  Matrix.CreateTranslation((size.Width - b.Width * scale) / 2, (size.Height - b.Height * scale) / 2);

        using var _ = context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.MediumQuality });
        foreach (var (e, o) in _leaves)
        {
            switch (e)
            {
                case ImageElement image:
                    {
                        var m = image.GetBasis();
                        var matrix = new Matrix(m.Item1, m.Item2, m.Item3, m.Item4, image.X + o.X, image.Y + o.Y) * fit;
                        // On-screen width of the image decides the decode width of its texture.
                        double px = Math.Sqrt(matrix.M11 * matrix.M11 + matrix.M12 * matrix.M12) * scaling;
                        complete &= DrawTexture(context, library, image.ClassName, matrix, px, image.Color.A / 255.0);
                        break;
                    }
                case SpawnElement:
                    {
                        if (SceneRenderer.MarkBitmap is not { } mark)
                            goto default;
                        var red = TintCache.Get(mark, Colors.Red) ?? mark;
                        var c = e.GetLocalCorners();
                        var r = new Rect(new Point(c[0].X + o.X, c[0].Y + o.Y).Transform(fit), new Point(c[2].X + o.X, c[2].Y + o.Y).Transform(fit));
                        context.DrawImage(red, new Rect(0, 0, red.PixelSize.Width, red.PixelSize.Height), r);
                        break;
                    }
                case ModelElement model when model.SilhouetteBounds != null && Silhouette(model, ref complete) is { } silhouette:
                    {
                        using (context.PushTransform(Matrix.CreateTranslation(model.X + o.X, model.Y + o.Y) * fit))
                            context.DrawGeometry(Brushes.Black, null, silhouette.Geometry);
                        break;
                    }
                case AnimationElement or ParticleElement:
                    {
                        var matrix = new Matrix(e.Width, 0, 0, e.Height, e.X + o.X, e.Y + o.Y) * fit;
                        complete &= DrawTexture(context, library, e.Name, matrix, matrix.M11 * scaling, 1);
                        break;
                    }
                default:
                    {
                        IBrush fill = e switch
                        {
                            TriggerElement => TriggerFill,
                            AreaElement => AreaFill,
                            PlatformElement or TrapezoidElement => CollisionFill,
                            _ => OtherFill,
                        };
                        var corners = e.GetLocalCorners();
                        var geometry = new StreamGeometry();
                        using (var g = geometry.Open())
                        {
                            g.BeginFigure(new Point(corners[0].X + o.X, corners[0].Y + o.Y).Transform(fit), true);
                            for (int i = 1; i < corners.Length; i++) g.LineTo(new Point(corners[i].X + o.X, corners[i].Y + o.Y).Transform(fit));
                            g.EndFigure(true);
                        }
                        context.DrawGeometry(fill, null, geometry);
                        break;
                    }
            }
        }
        return complete;
    }

    // ================= MODELS ================= //

    private sealed record ModelSilhouette(Geometry Geometry, Rect Bounds);

    /// <summary>Silhouettes per model library (shared by every preview), loaded once per model.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ModelLibrary, Dictionary<string, Task<ModelSilhouette?>>> Silhouettes = new();

    private readonly HashSet<string> _awaitedModels = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The model's silhouette, or null when it has none (unreadable, skipped) or while it loads.</summary>
    private ModelSilhouette? Silhouette(ModelElement model, ref bool complete)
    {
        var models = Models;
        string name = model.ClassName;
        if (models == null || string.IsNullOrEmpty(name) || ModelLibrary.IsSkipped(name))
            return null;
        var table = Silhouettes.GetValue(models, _ => new Dictionary<string, Task<ModelSilhouette?>>(StringComparer.OrdinalIgnoreCase));
        if (!table.TryGetValue(name, out var task))
            table[name] = task = LoadSilhouette(models, name);
        if (task.IsCompleted)
            return task.Result;
        complete = false;
        // Drawn again once it is there.
        if (_awaitedModels.Add(name))
            task.ContinueWith(_ => Dispatcher.UIThread.Post(() =>
            {
                _awaitedModels.Remove(name);
                _version++;
                InvalidateVisual();
            }), TaskScheduler.Default);
        return null;
    }

    private static async Task<ModelSilhouette?> LoadSilhouette(ModelLibrary models, string name)
    {
        try
        {
            var data = await models.GetAsync(name);
            if (data is not { IsSelfContained: true })
                return null;
            var geometry = data.BuildSilhouette(out var bounds);
            return new ModelSilhouette(geometry, bounds);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ObjectPreview] model {name}: {ex.Message}");
            return null;
        }
    }

    private static (double W, double H) NaturalSize(TextureLibrary library, TextureEntry entry)
    {
        // Static textures: read from the file's header (a full decode per texture would keep every texture of the
        // Project Window in memory); sprite sheets need their atlas.
        return library.TryGetSize(entry) is { } size ? (size.Width, size.Height) : (0, 0);
    }

    /// <summary>Draws a texture mapped onto the unit square; static textures use a thumbnail of about <paramref name="pixelWidth"/>.</summary>
    private static bool DrawTexture(DrawingContext context, TextureLibrary? library, string name, Matrix unitToTile, double pixelWidth, double opacity)
    {
        if (library?.Find(name) is not { } entry)
            return true;
        if (opacity <= 0)
            return true;
        if (entry.IsAnimated)
        {
            var asset = library.TryGet(name);
            if (asset == null) return false;
            using (context.PushTransform(unitToTile))
            using (context.PushOpacity(opacity))
                asset.Draw(context, new Rect(0, 0, 1, 1), 0);
            return true;
        }
        var thumb = library.TryGetThumbnail(entry, (int)Math.Ceiling(Math.Max(16, pixelWidth)));
        if (thumb == null) return false;
        using (context.PushTransform(unitToTile))
        using (context.PushOpacity(opacity))
            context.DrawImage(thumb, new Rect(0, 0, thumb.PixelSize.Width, thumb.PixelSize.Height), new Rect(0, 0, 1, 1));
        return true;
    }
}
