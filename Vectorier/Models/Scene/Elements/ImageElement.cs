using System;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Vectorier.Models.Scene;

public enum ImageType
{
    None = 0,
    Static = 1,
    Vanishing = 2,
    Dynamic = 3
}

/// <summary>Image Depth attribute: 0 = Front, absent = Middle, 1 = Back (as read by the game).</summary>
public enum ImageDepth
{
    Front,
    Middle,
    Back
}

/// <summary>
/// &lt;Image&gt;: a texture. Pivot is the top-left corner. Rotation and skew are expressed with Vector's
/// affine &lt;Matrix&gt; (width vector A,B and height vector C,D).
/// </summary>
public sealed partial class ImageElement : SceneElement
{
    public override ElementType Type => ElementType.Image;
    public override bool CanRotate => true;
    public override bool NameEditable => false;

    /// <summary>Rotation in degrees, Vector convention (positive turns clockwise on screen).</summary>
    [ObservableProperty] private double _rotation;

    /// <summary>Extra angle between the width and height axes, in degrees (0 = rectangle).</summary>
    [ObservableProperty] private double _skew;

    [ObservableProperty] private bool _flipX;
    [ObservableProperty] private bool _flipY;
    [ObservableProperty] private ImageType _imageType = ImageType.None;
    [ObservableProperty] private ImageDepth _depth = ImageDepth.Middle;
    [ObservableProperty] private Color _color = Colors.White;

    /// <summary>NativeX/NativeY from the XML (texture size). 0 when unknown.</summary>
    public int NativeWidth { get; set; }
    public int NativeHeight { get; set; }

    /// <summary>
    /// True when the XML had no Width/Height (the game then uses the texture size). The renderer fills
    /// Width/Height from the texture; the exporter omits them again while they still match.
    /// </summary>
    public bool AutoSize { get; set; }

    private bool _fillingNaturalSize;

    /// <summary>
    /// Gives an auto-sized image the size of its texture once it is known (render-time only: the image stays
    /// auto-sized, so its Width/Height are still omitted on export).
    /// </summary>
    public void FillNaturalSize(double width, double height)
    {
        _fillingNaturalSize = true;
        try
        {
            Width = width;
            Height = height;
        }
        finally
        {
            _fillingNaturalSize = false;
        }
    }

    // A size set by the user (tools, Inspector) must be written to the XML.
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_fillingNaturalSize && (e.PropertyName == nameof(Width) || e.PropertyName == nameof(Height)))
            AutoSize = false;
    }

    /// <summary>The XML used a &lt;Matrix&gt;; it keeps being written even when the image is axis aligned.</summary>
    public bool HasMatrix { get; set; }

    /// <summary>Id of the Platform whose surface generated this image (0 = an ordinary image). Editor only.</summary>
    public int SurfaceOwner { get; set; }

    /// <summary>The ClassName is the texture name, shown as the element name.</summary>
    public string ClassName
    {
        get => Name;
        set => Name = value;
    }

    public bool IsAxisAligned =>
        Math.Abs(Rotation) < 1e-6 && Math.Abs(Skew) < 1e-6 && !FlipX && !FlipY;

    /// <summary>Width vector (A,B) and height vector (C,D) in pixels.</summary>
    public (double A, double B, double C, double D) GetBasis()
    {
        double r = Rotation * Math.PI / 180.0;
        double s = Skew * Math.PI / 180.0;
        double fx = FlipX ? -1 : 1;
        double fy = FlipY ? -1 : 1;
        double a = Width * Math.Cos(r) * fx;
        double b = Width * Math.Sin(r) * fx;
        double c = -Height * Math.Sin(r + s) * fy;
        double d = Height * Math.Cos(r + s) * fy;
        return (a, b, c, d);
    }

    /// <summary>Sets the transform from a width vector and height vector.</summary>
    public void SetBasis(double a, double b, double c, double d)
    {
        double width = Math.Sqrt(a * a + b * b);
        double height = Math.Sqrt(c * c + d * d);
        double r = Math.Atan2(b, a);
        double angleV = Math.Atan2(d, c);
        double skew = NormalizeRadians(angleV - r - Math.PI / 2);
        bool flipY = false;
        if (Math.Abs(skew) > Math.PI / 2)
        {
            flipY = true;
            skew = NormalizeRadians(skew - Math.PI);
        }

        Width = width;
        Height = height;
        Rotation = Clean(r * 180.0 / Math.PI);
        Skew = Clean(skew * 180.0 / Math.PI);
        FlipX = false;
        FlipY = flipY;
    }

    private static double Clean(double degrees) => Math.Abs(degrees) < 1e-7 ? 0 : Math.Round(degrees, 6);

    private static double NormalizeRadians(double angle)
    {
        while (angle > Math.PI) angle -= 2 * Math.PI;
        while (angle <= -Math.PI) angle += 2 * Math.PI;
        return angle;
    }

    public override Point[] GetLocalCorners()
    {
        var (a, b, c, d) = GetBasis();
        var p = new Point(X, Y);
        return new[]
        {
            p,
            new Point(X + a, Y + b),
            new Point(X + a + c, Y + b + d),
            new Point(X + c, Y + d),
        };
    }

    public override void ScaleAround(Point pivot, double sx, double sy)
    {
        var (a, b, c, d) = GetBasis();
        X = pivot.X + (X - pivot.X) * sx;
        Y = pivot.Y + (Y - pivot.Y) * sy;
        if (IsAxisAligned && sx >= 0 && sy >= 0)
        {
            Width *= sx;
            Height *= sy;
            return;
        }
        SetBasis(a * sx, b * sy, c * sx, d * sy);
    }

    public override void RotateAround(Point pivot, double radians)
    {
        base.RotateAround(pivot, radians);
        Rotation = Clean(Rotation + radians * 180.0 / Math.PI);
    }

    protected override SceneElement CreateEmpty() => new ImageElement();

    protected override void CopyTo(SceneElement target)
    {
        base.CopyTo(target);
        var t = (ImageElement)target;
        t.Rotation = Rotation;
        t.Skew = Skew;
        t.FlipX = FlipX;
        t.FlipY = FlipY;
        t.ImageType = ImageType;
        t.Depth = Depth;
        t.Color = Color;
        t.NativeWidth = NativeWidth;
        t.NativeHeight = NativeHeight;
        t.AutoSize = AutoSize;
        t.HasMatrix = HasMatrix;
        t.SurfaceOwner = SurfaceOwner;
        // A platform's generated textures stay locked in their copies (the platform is what you move and resize).
        if (SurfaceOwner != 0)
            t.IsLocked = IsLocked;
    }
}
