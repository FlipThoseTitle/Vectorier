using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Controls.Scene;
using Vectorier.Models.Scene;
using Vectorier.Services;
using Vectorier.Services.Assets;
using Vectorier.Services.IO;
using Vectorier.ViewModels.Dialogs;
using Vectorier.Services.Localization;

namespace Vectorier.ViewModels.Editor;

/// <summary>
/// The Brush tool. A paint session is a canvas drawn above everything in the Scene: a new image (Brush click on
/// the Scene, named first), one image being edited, or several images merged into one. The edited images are
/// hidden while painting; Apply writes the PNG and brings the usual image back.
/// </summary>
public sealed partial class PaintViewModel : ObservableObject
{
    private enum ApplyChoice { Cancel, Apply, Duplicate }

    private static readonly Regex ValidName = new("^[A-Za-z0-9_\\-]+$", RegexOptions.Compiled);

    private readonly EditorViewModel _editor;
    private readonly HashSet<SceneElement> _hidden = new();

    public PaintViewModel(EditorViewModel editor)
    {
        _editor = editor;
    }

    public static IReadOnlyList<BrushKind> BrushKinds { get; } = Enum.GetValues<BrushKind>();
    /// <summary>Blending modes of the dropdown (erasing is the separate Erase option).</summary>
    public static IReadOnlyList<PaintBlendMode> BlendModes { get; } = Enum.GetValues<PaintBlendMode>().Where(m => m != PaintBlendMode.Erase).ToList();

    // ================= SETTINGS ================= //

    [ObservableProperty] private BrushKind _kind = BrushKind.Hard;

    /// <summary>Brush diameter in level pixels.</summary>
    [ObservableProperty] private double _size = 40;

    /// <summary>0 – 100 %.</summary>
    [ObservableProperty] private double _opacity = 100;

    [ObservableProperty] private Color _color = Colors.Black;
    [ObservableProperty] private PaintBlendMode _blend = PaintBlendMode.Normal;

    /// <summary>The brush removes paint (with its shape, size and opacity) instead of adding color.</summary>
    [ObservableProperty] private bool _erase;

    /// <summary>Keep the paint inside the edited image (off: paint freely, the image grows).</summary>
    [ObservableProperty] private bool _clipToBounds;

    public const double MinSize = 1, MaxSize = 1000;

    partial void OnSizeChanged(double value)
    {
        double clamped = Math.Clamp(Math.Round(value, 1), MinSize, MaxSize);
        if (Math.Abs(clamped - value) > 1e-9)
            Size = clamped;
        _editor.InvalidateScene();
    }

    partial void OnClipToBoundsChanged(bool value)
    {
        if (Canvas != null)
            Canvas.ClipToOriginal = value && !IsNew;
        _editor.InvalidateScene();
    }

    /// <summary>[ / ] : smaller / bigger brush.</summary>
    public void StepSize(int direction)
    {
        double step = Size < 10 ? 1 : Size < 50 ? 5 : Size < 200 ? 10 : 50;
        Size = Math.Clamp(Size + direction * step, MinSize, MaxSize);
        _editor.StatusText = Loc.F("Brush size: {0:0.#}", Size);
    }

    public BrushSettings Settings => new(Kind, Size, Opacity / 100.0, Color, Erase ? PaintBlendMode.Erase : Blend);

    // ================= SESSION ================= //

    public PaintCanvas? Canvas { get; private set; }

    /// <summary>Parallax factor of the layer the canvas is in.</summary>
    public double LayerFactor { get; private set; }

    /// <summary>The images being edited (empty for a new image). The first one receives the result.</summary>
    public IReadOnlyList<ImageElement> Targets { get; private set; } = Array.Empty<ImageElement>();

    /// <summary>Texture name of a new image (asked when the session starts).</summary>
    public string NewName { get; private set; } = string.Empty;

    public bool IsActive => Canvas != null;
    public bool IsNew => IsActive && Targets.Count == 0;
    public bool IsMerge => Targets.Count > 1;
    public bool CanClip => IsActive && !IsNew;

    /// <summary>Size of what Apply will save (follows painting and undo).</summary>
    private string PaintedSize => Canvas?.PaintedBounds is { } b ? $"{b.Width}×{b.Height}" : "empty";

    public string SessionTitle => Canvas == null ? string.Empty
        : IsNew ? Loc.F("New image '{0}' ({1})", NewName, PaintedSize)
        : IsMerge ? Loc.F("Merging {0} images ({1})", Targets.Count, PaintedSize)
        : Loc.F("Editing '{0}' ({1})", Targets[0].ClassName, PaintedSize);

    public bool IsHidden(SceneElement element) => _hidden.Count > 0 && _hidden.Contains(element);

    private void SetSession(PaintCanvas? canvas, IReadOnlyList<ImageElement> targets, double factor, string newName)
    {
        Canvas = canvas;
        Targets = targets;
        LayerFactor = factor;
        NewName = newName;
        _hidden.Clear();
        foreach (var t in targets)
            _hidden.Add(t);
        if (canvas != null)
            canvas.ClipToOriginal = ClipToBounds && targets.Count > 0;
        OnPropertyChanged(nameof(Canvas));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(IsMerge));
        OnPropertyChanged(nameof(CanClip));
        OnPropertyChanged(nameof(SessionTitle));
        _editor.RaiseStructureChanged(); // the renderer hides / shows the edited images
        _editor.InvalidateScene();
    }

    /// <summary>Ends the session without writing anything (level closed, object edit entered...).</summary>
    public void Reset()
    {
        if (IsActive)
            SetSession(null, Array.Empty<ImageElement>(), 0, string.Empty);
    }

    /// <summary>Asks whether to throw the painting away. True when there is nothing left to lose.</summary>
    public async Task<bool> ConfirmDiscardAsync()
    {
        if (!IsActive)
            return true;
        string what = IsNew ? $"the new image '{NewName}'" : "the changes to the image";
        bool discard = await AppServices.Dialogs.ConfirmAsync(Loc.T("Brush"), Loc.F("Discard {0}?", what), Loc.T("Discard"), Loc.T("Keep Editing"), MessageKind.Warning);
        if (discard)
            Discard();
        return discard;
    }

    [RelayCommand]
    private void Discard()
    {
        if (!IsActive)
            return;
        bool wasNew = IsNew;
        Reset();
        _editor.StatusText = wasNew ? Loc.T("New image discarded.") : Loc.T("Painting discarded.");
    }

    // ================= START ================= //

    private string? ValidateName(string name)
    {
        name = name.Trim();
        if (name.Length == 0)
            return Loc.T("Enter a name.");
        if (!ValidName.IsMatch(name))
            return Loc.T("Use letters, digits, '_' and '-' only.");
        if (_editor.Context.Textures.Find(name) != null)
            return Loc.F("A texture named '{0}' already exists.", name);
        return null;
    }

    /// <summary>Brush click with nothing being painted: asks for the new image's name and opens a canvas there.</summary>
    public async Task<bool> StartNewAsync(Point level)
    {
        if (!_editor.Context.Textures.IsIndexed)
            await _editor.Context.Textures.RefreshIndexAsync();
        string? name = await AppServices.Dialogs.PromptAsync(Loc.T("New Image"), Loc.T("Name of the new image (PNG in the project's textures):"),
            NextFreeName("paint"), ValidateName, Loc.T("Start Painting"));
        if (string.IsNullOrWhiteSpace(name))
            return false;
        name = name.Trim();
        const int start = 512;
        var origin = new Point(Math.Round(level.X - start / 2.0), Math.Round(level.Y - start / 2.0));
        var canvas = new PaintCanvas(start, start, Matrix.CreateTranslation(origin.X, origin.Y), canGrow: true);
        // New images go to the gameplay layer.
        SetSession(canvas, Array.Empty<ImageElement>(), Layers.GetFactor(Layers.Default), name);
        _editor.StatusText = Loc.F("Painting '{0}': paint anywhere, then press Apply.", name);
        return true;
    }

    private string NextFreeName(string stem)
    {
        var textures = _editor.Context.Textures;
        for (int i = 1; ; i++)
        {
            string name = $"{stem}_{i:00}";
            if (textures.Find(name) == null)
                return name;
        }
    }

    /// <summary>Edit Image (Inspector, Hierarchy): the selected images, merged into one when there are several.</summary>
    public async Task EditSelectionAsync()
    {
        var images = _editor.GetTopmostSelection().OfType<ImageElement>().ToList();
        if (images.Count == 0)
            images = _editor.Selection.OfType<ImageElement>().ToList();
        if (images.Count == 0)
        {
            _editor.StatusText = Loc.T("Select one or more images to edit.");
            return;
        }
        if (IsActive && !await ConfirmDiscardAsync())
            return;
        await StartEditAsync(images);
    }

    public async Task<bool> StartEditAsync(IReadOnlyList<ImageElement> images)
    {
        var textures = _editor.Context.Textures;
        var assets = new List<(ImageElement Image, TextureAsset Asset)>();
        foreach (var image in images)
        {
            var asset = await textures.GetAsync(image.ClassName);
            if (asset == null)
            {
                await AppServices.Dialogs.ShowMessageAsync(Loc.T("Edit Image"), Loc.F("The texture '{0}' could not be loaded.", image.ClassName), MessageKind.Warning);
                return false;
            }
            if (asset.IsAnimated)
            {
                await AppServices.Dialogs.ShowMessageAsync(Loc.T("Edit Image"), Loc.F("'{0}' is a sprite sheet (animated texture); only still images can be painted.", image.ClassName), MessageKind.Warning);
                return false;
            }
            assets.Add((image, asset));
        }

        // Draw order: the order the Scene draws them (the first one painted is at the bottom).
        var order = _editor.Document.AllElements().Select((e, i) => (e, i)).ToDictionary(t => t.e, t => t.i);
        assets = assets.OrderBy(a => a.Image.Root.Order).ThenBy(a => order.GetValueOrDefault(a.Image)).ToList();
        var primary = images.Contains(_editor.ActiveElement as ImageElement) ? (ImageElement)_editor.ActiveElement! : images[0];

        double factor = Layers.GetFactor(primary.EffectiveLayer);
        if (assets.Any(a => Math.Abs(Layers.GetFactor(a.Image.EffectiveLayer) - factor) > 1e-9))
        {
            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Edit Image"), Loc.T("The images to merge must be in the same layer."), MessageKind.Warning);
            return false;
        }

        PaintCanvas canvas;
        if (assets.Count == 1)
        {
            var (image, asset) = assets[0];
            int w = asset.Width, h = asset.Height;
            if (w > PaintCanvas.MaxSize || h > PaintCanvas.MaxSize)
            {
                await AppServices.Dialogs.ShowMessageAsync(Loc.T("Edit Image"), Loc.F("'{0}' is {1}×{2}; the Brush edits images up to {3}×{4}.", image.ClassName, w, h, PaintCanvas.MaxSize, PaintCanvas.MaxSize), MessageKind.Warning);
                return false;
            }
            var (a, b, c, d) = image.GetBasis();
            var o = image.ParentWorldPosition;
            var toLevel = new Matrix(a / w, b / w, c / h, d / h, o.X + image.X, o.Y + image.Y);
            canvas = new PaintCanvas(w, h, toLevel, canGrow: image.IsAxisAligned);
            canvas.Load(Rasterize(w, h, ctx => asset.Draw(ctx, new Rect(0, 0, w, h))));
        }
        else
        {
            // Merge: an unrotated canvas over all of them, one level pixel per pixel.
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var (image, _) in assets)
            {
                var o = image.ParentWorldPosition;
                foreach (var p in image.GetLocalCorners())
                {
                    minX = Math.Min(minX, p.X + o.X); maxX = Math.Max(maxX, p.X + o.X);
                    minY = Math.Min(minY, p.Y + o.Y); maxY = Math.Max(maxY, p.Y + o.Y);
                }
            }
            minX = Math.Floor(minX); minY = Math.Floor(minY);
            int w = Math.Max(1, (int)Math.Ceiling(maxX - minX)), h = Math.Max(1, (int)Math.Ceiling(maxY - minY));
            if (w > PaintCanvas.MaxSize || h > PaintCanvas.MaxSize)
            {
                await AppServices.Dialogs.ShowMessageAsync(Loc.T("Edit Image"), Loc.F("Merged, these images cover {0}×{1}; the Brush makes images up to {2}×{3}.", w, h, PaintCanvas.MaxSize, PaintCanvas.MaxSize), MessageKind.Warning);
                return false;
            }
            canvas = new PaintCanvas(w, h, Matrix.CreateTranslation(minX, minY), canGrow: true);
            canvas.Load(Rasterize(w, h, ctx =>
            {
                foreach (var (image, asset) in assets)
                {
                    var o = image.ParentWorldPosition;
                    var (a, b, c, d) = image.GetBasis();
                    var unit = new Matrix(a, b, c, d, o.X + image.X - minX, o.Y + image.Y - minY);
                    if (image.Color.A == 0)
                        continue;
                    // The tint is baked into the merged image.
                    var tinted = TintCache.NeedsTint(image.Color) ? TintCache.Get(asset.Bitmap, image.Color) : null;
                    using (ctx.PushTransform(unit))
                    using (ctx.PushOpacity(image.Color.A / 255.0))
                        asset.Draw(ctx, new Rect(0, 0, 1, 1), 0, tinted);
                }
            }));
        }

        var targets = new List<ImageElement> { primary };
        targets.AddRange(assets.Select(x => x.Image).Where(i => !ReferenceEquals(i, primary)));
        SetSession(canvas, targets, factor, string.Empty);
        if (_editor.ActiveTool != EditorTool.Brush)
            _editor.ActiveTool = EditorTool.Brush;
        _editor.StatusText = IsMerge
            ? Loc.F("Merged {0} images: paint, then press Apply.", targets.Count)
            : Loc.F("Editing '{0}': paint, then press Apply.", primary.ClassName);
        return true;
    }

    /// <summary>Draws into a transparent bitmap and returns its premultiplied BGRA pixels.</summary>
    private static byte[] Rasterize(int width, int height, Action<DrawingContext> draw)
    {
        using var target = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        using (var ctx = target.CreateDrawingContext(true))
            draw(ctx);
        using var copy = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        var pixels = new byte[width * height * 4];
        using (var fb = copy.Lock())
        {
            target.CopyPixels(fb);
            for (int y = 0; y < height; y++)
                Marshal.Copy(fb.Address + y * fb.RowBytes, pixels, y * width * 4, width * 4);
        }
        return pixels;
    }

    // ================= STROKES ================= //

    public bool IsStroking { get; private set; }

    public void BeginStroke(Point level)
    {
        if (Canvas == null)
            return;
        Canvas.ClipToOriginal = ClipToBounds && !IsNew;
        Canvas.BeginStroke(Settings, Canvas.LevelToCanvas(level));
        IsStroking = true;
        _editor.InvalidateScene();
    }

    public void StrokeTo(Point level)
    {
        if (Canvas == null || !IsStroking)
            return;
        Canvas.StrokeTo(Canvas.LevelToCanvas(level));
        _editor.InvalidateScene();
    }

    public void Spray()
    {
        if (Canvas == null || !IsStroking)
            return;
        Canvas.Spray();
        _editor.InvalidateScene();
    }

    public void EndStroke()
    {
        if (Canvas == null || !IsStroking)
            return;
        Canvas.EndStroke();
        IsStroking = false;
        OnPropertyChanged(nameof(SessionTitle));
        _editor.InvalidateScene();
    }

    /// <summary>Ctrl+Z while painting: takes back the last stroke.</summary>
    public bool Undo()
    {
        if (Canvas == null)
            return false;
        if (Canvas.Undo())
        {
            OnPropertyChanged(nameof(SessionTitle));
            _editor.InvalidateScene();
            _editor.StatusText = Loc.T("Stroke undone");
        }
        else
        {
            _editor.StatusText = Loc.T("Nothing to undo in this painting");
        }
        return true;
    }

    // ================= APPLY ================= //

    [RelayCommand]
    private async Task Apply()
    {
        if (Canvas is not { } canvas)
            return;
        if (IsStroking)
            EndStroke();

        var choice = await AskApplyAsync();
        if (choice == ApplyChoice.Cancel)
            return;

        // What is kept: everything painted, plus the edited image's own area.
        var content = canvas.ContentBounds();
        PixelRect crop;
        if (IsNew || IsMerge)
        {
            if (content is not { } c)
            {
                await AppServices.Dialogs.ShowMessageAsync(Loc.T("Brush"), Loc.T("Nothing has been painted yet."), MessageKind.Info);
                return;
            }
            crop = c;
        }
        else
        {
            crop = content is { } c ? canvas.Original.Union(c) : canvas.Original;
        }

        string name;
        if (IsNew)
        {
            name = NewName;
        }
        else if (choice == ApplyChoice.Duplicate)
        {
            var newName = await AppServices.Dialogs.PromptAsync(Loc.T("Save as Duplicate"), Loc.T("Name of the new image:"),
                NextFreeName(Targets[0].ClassName), ValidateName, Loc.T("Save"));
            if (string.IsNullOrWhiteSpace(newName))
                return;
            name = newName.Trim();
        }
        else
        {
            name = Targets[0].ClassName;
        }

        var textures = _editor.Context.Textures;
        try
        {
            byte[] png = canvas.EncodePng(crop);
            var entry = textures.Find(name);
            string path = entry is { IsAnimated: false } existing && existing.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                ? existing.Path
                : VPath.Combine(_editor.Project.TexturesFolder, name + ".png");
            await AppServices.FileSystem.CreateDirectoryAsync(VPath.GetDirectory(path));
            await AppServices.FileSystem.WriteAllBytesAsync(path, png);
            textures.Invalidate(name);
            await textures.RefreshIndexAsync();
            await textures.GetAsync(name);
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Brush"), Loc.F("Could not save '{0}.png': {1}", name, ex.Message), MessageKind.Error);
            return;
        }

        var origin = canvas.CanvasToLevel.Transform(new Point(crop.X, crop.Y));
        _editor.BeginGesture();
        try
        {
            if (IsNew)
            {
                var image = new ImageElement
                {
                    Name = name,
                    X = Math.Round(origin.X),
                    Y = Math.Round(origin.Y),
                    Width = crop.Width,
                    Height = crop.Height,
                    NativeWidth = crop.Width,
                    NativeHeight = crop.Height,
                };
                image.AutoSize = false;
                _editor.Document.Insert(image, null);
                _editor.RaiseStructureChanged();
                _editor.Select(image);
            }
            else
            {
                var primary = Targets[0];
                var parent = primary.ParentWorldPosition;
                if (IsMerge)
                {
                    primary.Rotation = 0;
                    primary.Skew = 0;
                    primary.FlipX = false;
                    primary.FlipY = false;
                    primary.Color = Colors.White; // baked in
                }
                if (IsMerge || canvas.Original != crop)
                {
                    // Axis-aligned result: scale of the canvas on each axis.
                    double sx = canvas.CanvasToLevel.M11, sy = canvas.CanvasToLevel.M22;
                    if (IsMerge || primary.IsAxisAligned)
                    {
                        primary.X = Math.Round(origin.X - parent.X, 3);
                        primary.Y = Math.Round(origin.Y - parent.Y, 3);
                        primary.Width = crop.Width * sx;
                        primary.Height = crop.Height * sy;
                    }
                }
                primary.ClassName = name;
                primary.NativeWidth = crop.Width;
                primary.NativeHeight = crop.Height;
                primary.AutoSize = false;
                foreach (var other in Targets.Skip(1))
                    _editor.Document.Remove(other);
                if (Targets.Count > 1)
                    _editor.RaiseStructureChanged();
                _editor.Select(primary);
            }
            _editor.MarkChanged();
        }
        finally
        {
            _editor.EndGesture();
        }

        bool merged = IsMerge;
        bool newFile = IsNew || choice == ApplyChoice.Duplicate;
        Reset();
        // A new PNG shows up (selected) in the Project Window's textures.
        if (newFile && _editor.ProjectWindow.Context == PwContext.Textures)
        {
            await _editor.ProjectWindow.ReloadAsync();
            if (_editor.Context.Textures.Find(name) is { } added)
                await _editor.ProjectWindow.RevealNewAsync(new[] { added.FileName });
        }
        _editor.SelectTool(EditorTool.Move);
        _editor.StatusText = merged ? Loc.F("Merged into '{0}.png'.", name) : Loc.F("Saved '{0}.png'.", name);
    }

    private async Task<ApplyChoice> AskApplyAsync()
    {
        string message = IsNew
            ? Loc.F("Save the painting as '{0}.png' and place it in the level?", NewName)
            : IsMerge
                ? Loc.F("Apply writes the merged image into '{0}.png' (every level using it changes) and removes the other {1} image(s). Save as Duplicate writes a new PNG instead.", Targets[0].ClassName, Targets.Count - 1)
                : Loc.F("Apply writes the changes into '{0}.png' (every level using it changes). Save as Duplicate writes a new PNG and points this image at it.", Targets[0].ClassName);
        var dialog = new MessageDialogViewModel(Loc.T("Apply Painting"), message, MessageKind.Question);
        dialog.Buttons.Add(new DialogButton(Loc.T("Cancel"), ApplyChoice.Cancel, isCancel: true));
        dialog.Buttons.Add(new DialogButton(Loc.T("Apply"), ApplyChoice.Apply, isDefault: true));
        if (!IsNew)
            dialog.Buttons.Add(new DialogButton(Loc.T("Save as Duplicate"), ApplyChoice.Duplicate));
        return await AppServices.Dialogs.ShowAsync(dialog) is ApplyChoice choice ? choice : ApplyChoice.Cancel;
    }
}
