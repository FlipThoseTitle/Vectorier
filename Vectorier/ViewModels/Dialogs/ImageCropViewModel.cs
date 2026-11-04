using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Services.Localization;

namespace Vectorier.ViewModels.Dialogs;

/// <summary>What fills the parts of the canvas the image does not cover.</summary>
public enum CropBackground
{
    Transparent,
    Black,
    White,
}

public sealed record CropBackgroundOption(CropBackground Value, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Frames a picked image on a canvas of the image's recommended resolution (a level thumbnail is 512 × 340, a
/// project thumbnail 960 × 540...). The image keeps its aspect ratio: it starts scaled to fit entirely inside the
/// canvas, and can be moved and zoomed. Apply renders the canvas to a PNG of exactly that resolution (the result).
/// </summary>
public sealed partial class ImageCropViewModel : DialogViewModel
{
    /// <summary>Smallest and largest zoom, relative to "fit" (the whole image inside the canvas).</summary>
    private const double MinZoom = 0.1, MaxZoom = 20;

    public ImageCropViewModel(Bitmap source, int width, int height, string imageName)
    {
        Source = source;
        TargetWidth = Math.Max(1, width);
        TargetHeight = Math.Max(1, height);
        Title = Loc.F("Crop Image - {0}", imageName);
        SizeText = Loc.F("Image: {0} × {1} px   →   Saved as: {2} × {3} px", source.PixelSize.Width, source.PixelSize.Height, TargetWidth, TargetHeight);
        BackgroundOptions = new List<CropBackgroundOption>
        {
            new(CropBackground.Transparent, Loc.T("Transparent")),
            new(CropBackground.Black, Loc.T("Black")),
            new(CropBackground.White, Loc.T("White")),
        };
        _selectedBackground = BackgroundOptions[0];
        _scale = FitScale;
        _center = new Point(TargetWidth / 2.0, TargetHeight / 2.0);
    }

    public Bitmap Source { get; }
    public int TargetWidth { get; }
    public int TargetHeight { get; }
    public string SizeText { get; }

    public override bool Resizable => true;
    public override double PreferredWidth => 900;
    public override double PreferredHeight => 680;

    public double SourceWidth => Source.PixelSize.Width;
    public double SourceHeight => Source.PixelSize.Height;

    /// <summary>Scale at which the whole image fits inside the canvas.</summary>
    public double FitScale => Math.Min(TargetWidth / SourceWidth, TargetHeight / SourceHeight);

    /// <summary>Scale at which the image covers the whole canvas (the edges are cropped).</summary>
    public double FillScale => Math.Max(TargetWidth / SourceWidth, TargetHeight / SourceHeight);

    /// <summary>Canvas pixels per image pixel (the same horizontally and vertically: the image is never stretched).</summary>
    [ObservableProperty] private double _scale;

    /// <summary>Where the image's middle is, in canvas pixels.</summary>
    [ObservableProperty] private Point _center;

    [ObservableProperty] private CropBackgroundOption _selectedBackground;

    public IReadOnlyList<CropBackgroundOption> BackgroundOptions { get; }

    [ObservableProperty] private bool _isBusy;

    /// <summary>Raised when the framing changes (the canvas redraws).</summary>
    public event Action? Changed;

    /// <summary>The image's rectangle on the canvas, in canvas pixels.</summary>
    public Rect ImageRect => new(Center.X - SourceWidth * Scale / 2, Center.Y - SourceHeight * Scale / 2, SourceWidth * Scale, SourceHeight * Scale);

    /// <summary>Zoom slider: log2 of the zoom relative to "fit", so each step doubles or halves.</summary>
    public double ZoomLog
    {
        get => Math.Log2(Scale / FitScale);
        set => Scale = FitScale * Math.Pow(2, value);
    }

    public double ZoomLogMin => Math.Log2(MinZoom);
    public double ZoomLogMax => Math.Log2(Math.Max(MaxZoom, FillScale / FitScale * 4));

    public string ZoomText => Loc.F("Zoom: {0:0}%", Scale * 100);

    partial void OnScaleChanged(double value)
    {
        double clamped = Math.Clamp(value, FitScale * MinZoom, FitScale * Math.Pow(2, ZoomLogMax));
        if (Math.Abs(clamped - value) > 1e-12)
        {
            Scale = clamped;
            return;
        }
        OnPropertyChanged(nameof(ZoomLog));
        OnPropertyChanged(nameof(ZoomText));
        Changed?.Invoke();
    }

    partial void OnCenterChanged(Point value) => Changed?.Invoke();

    partial void OnSelectedBackgroundChanged(CropBackgroundOption value) => Changed?.Invoke();

    /// <summary>Zooms keeping the canvas point <paramref name="anchor"/> where it is (the point under the pointer).</summary>
    public void ZoomAt(Point anchor, double factor)
    {
        double old = Scale;
        Scale = old * factor;
        double applied = Scale / old;
        Center = anchor + (Center - anchor) * applied;
    }

    /// <summary>The whole image inside the canvas, centered (how the window opens).</summary>
    [RelayCommand]
    private void Fit()
    {
        Scale = FitScale;
        Center = new Point(TargetWidth / 2.0, TargetHeight / 2.0);
    }

    /// <summary>The image covers the whole canvas, centered (the edges that stick out are cropped).</summary>
    [RelayCommand]
    private void Fill()
    {
        Scale = FillScale;
        Center = new Point(TargetWidth / 2.0, TargetHeight / 2.0);
    }

    [RelayCommand]
    private void CenterImage() => Center = new Point(TargetWidth / 2.0, TargetHeight / 2.0);

    public IBrush? BackgroundBrush => SelectedBackground.Value switch
    {
        CropBackground.Black => Brushes.Black,
        CropBackground.White => Brushes.White,
        _ => null,
    };

    /// <summary>The canvas at its exact resolution, as PNG bytes.</summary>
    public byte[] RenderPng()
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(TargetWidth, TargetHeight), new Vector(96, 96));
        using (var ctx = bitmap.CreateDrawingContext(true))
        {
            if (BackgroundBrush is { } background)
                ctx.FillRectangle(background, new Rect(0, 0, TargetWidth, TargetHeight));
            using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
                ctx.DrawImage(Source, new Rect(0, 0, SourceWidth, SourceHeight), ImageRect);
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        return stream.ToArray();
    }

    [RelayCommand]
    private void Apply()
    {
        if (IsBusy)
            return;
        IsBusy = true;
        try
        {
            Close(RenderPng());
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelCrop() => Close(null);
}
