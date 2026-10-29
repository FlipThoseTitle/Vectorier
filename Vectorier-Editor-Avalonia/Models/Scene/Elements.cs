using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Vectorier_Editor_Avalonia.Models.Scene;

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
    }
}

/// <summary>&lt;Trigger&gt;: a rectangle whose Content holds game logic.</summary>
public sealed partial class TriggerElement : SceneElement
{
    public override ElementType Type => ElementType.Trigger;

    /// <summary>Inner XML of &lt;Content&gt; (Init, Loop, Template...).</summary>
    [ObservableProperty] private string _contentXml = string.Empty;

    /// <summary>
    /// Editor-only: the type of each variable in the Trigger Editor (Unity's TriggerComponent.variableTypes).
    /// Never exported; it only changes which value editor is shown.
    /// </summary>
    public Dictionary<string, string> VariableTypes { get; set; } = new();

    /// <summary>
    /// Key of this trigger in the editor cache of variable types: its name plus a hash of its content with
    /// formatting removed, so the same trigger is found again after saving, reopening, copying or storing it.
    /// </summary>
    public string TypeCacheKey => Name + "@" + ContentHash(ContentXml);

    public static string ContentHash(string content)
    {
        string canonical;
        try
        {
            var root = XElement.Parse("<C>" + content + "</C>", LoadOptions.None);
            canonical = root.ToString(SaveOptions.DisableFormatting);
        }
        catch
        {
            canonical = string.Join(" ", (content ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }
        // FNV-1a 64-bit (stable across runs and platforms, no crypto needed in the browser).
        ulong hash = 14695981039346656037UL;
        foreach (char ch in canonical)
        {
            hash ^= ch;
            hash *= 1099511628211UL;
        }
        return hash.ToString("x16");
    }

    protected override SceneElement CreateEmpty() => new TriggerElement();

    protected override void CopyTo(SceneElement target)
    {
        base.CopyTo(target);
        var t = (TriggerElement)target;
        t.ContentXml = ContentXml;
        t.VariableTypes = new Dictionary<string, string>(VariableTypes);
    }
}

/// <summary>&lt;Area&gt;: animation / catch / trick / help regions.</summary>
public sealed partial class AreaElement : SceneElement
{
    public static readonly string[] KnownTypes = { "Animation", "Catch", "Trick", "Help" };

    public override ElementType Type => ElementType.Area;

    [ObservableProperty] private string _areaType = "Animation";
    [ObservableProperty] private int _distance = 300;
    [ObservableProperty] private string _itemName = "TRICK_";
    [ObservableProperty] private int _score = 100;
    [ObservableProperty] private string _key = "Up";
    [ObservableProperty] private string _description = string.Empty;

    /// <summary>Trick Areas always have the game's trick size.</summary>
    public const double TrickWidth = 150, TrickHeight = 200;

    public bool IsTrick => AreaType == "Trick";

    /// <summary>Trick Areas have a fixed size, and their name comes from moves.xml's TrickGroup (picked, not typed).</summary>
    public override bool CanResize => !IsTrick;
    public override bool NameEditable => !IsTrick;

    partial void OnAreaTypeChanged(string value)
    {
        if (value == "Trick")
        {
            Width = TrickWidth;
            Height = TrickHeight;
        }
        OnPropertyChanged(nameof(IsTrick));
    }

    public override void ScaleAround(Point pivot, double sx, double sy)
    {
        if (!IsTrick)
        {
            base.ScaleAround(pivot, sx, sy);
            return;
        }
        // Only the position follows (the size stays 150 × 200).
        X = pivot.X + (X - pivot.X) * sx;
        Y = pivot.Y + (Y - pivot.Y) * sy;
    }

    protected override SceneElement CreateEmpty() => new AreaElement();

    protected override void CopyTo(SceneElement target)
    {
        base.CopyTo(target);
        var t = (AreaElement)target;
        t.AreaType = AreaType;
        t.Distance = Distance;
        t.ItemName = ItemName;
        t.Score = Score;
        t.Key = Key;
        t.Description = Description;
    }
}

/// <summary>&lt;Platform&gt;: rectangular collision. Position and size are integers.</summary>
public sealed class PlatformElement : SceneElement
{
    public override ElementType Type => ElementType.Platform;
    public override bool IntegerTransform => true;
    public override bool LayerEditable => false;

    private Services.Assets.PlatformSurfaceSettings? _surface;

    /// <summary>
    /// Rough / Smooth / Block dressing (Unity's PlatformComponent). Editor only: the textures it generates are
    /// ordinary Images next to the Platform and are rebuilt whenever the Platform or these settings change.
    /// </summary>
    public Services.Assets.PlatformSurfaceSettings? Surface
    {
        get => _surface;
        set => SetProperty(ref _surface, value);
    }

    /// <summary>What the current tiles were built from (<see cref="Services.Assets.PlatformSurface.BuildKey"/>).</summary>
    public string? SurfaceBuiltKey { get; set; }

    protected override SceneElement CreateEmpty() => new PlatformElement();

    protected override void CopyTo(SceneElement target)
    {
        base.CopyTo(target);
        var t = (PlatformElement)target;
        t.Surface = Surface;
        t.SurfaceBuiltKey = SurfaceBuiltKey;
    }
}

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

/// <summary>Base for elements that are a single point (Camera, Spawn, Item, Model).</summary>
public abstract class PointElement : SceneElement
{
    public override bool HasSize => false;
    public override bool LayerEditable => false;

    /// <summary>Half-size of the box used for picking and selection outlines.</summary>
    public virtual Size HandleSize => new(80, 80);

    public override Point[] GetLocalCorners()
    {
        var s = HandleSize;
        double hw = s.Width / 2, hh = s.Height / 2;
        return new[]
        {
            new Point(X - hw, Y - hh),
            new Point(X + hw, Y - hh),
            new Point(X + hw, Y + hh),
            new Point(X - hw, Y + hh),
        };
    }

    public override void ScaleAround(Point pivot, double sx, double sy)
    {
        X = pivot.X + (X - pivot.X) * sx;
        Y = pivot.Y + (Y - pivot.Y) * sy;
    }
}

/// <summary>
/// In / Out point of a Building definition, edited in Edit Object mode. Not an XML element: written as the
/// definition's InX / InY / OutX / OutY. Center pivot, cannot be renamed, never listed in the Hierarchy.
/// </summary>
public sealed class BuildingMarkerElement : PointElement
{
    public BuildingMarkerElement(bool isIn)
    {
        IsIn = isIn;
        Name = isIn ? "In" : "Out";
    }

    public bool IsIn { get; }
    public override ElementType Type => IsIn ? ElementType.BuildingIn : ElementType.BuildingOut;
    public override bool NameEditable => false;
    public override bool SupportsDynamics => false;
    public override Size HandleSize => new(100, 100);

    protected override SceneElement CreateEmpty() => new BuildingMarkerElement(IsIn);
}

/// <summary>&lt;Camera&gt;: only its position is stored.</summary>
public sealed class CameraElement : PointElement
{
    /// <summary>Visible area of the Unity orthographic camera (size 400 at 16:9).</summary>
    public static readonly Size ViewSize = new(800.0 * 16.0 / 9.0, 800.0);

    public override ElementType Type => ElementType.Camera;
    public override bool NameEditable => false;
    public override bool SupportsDynamics => false;
    public override Size HandleSize => new(120, 120);

    protected override SceneElement CreateEmpty() => new CameraElement();
}

/// <summary>&lt;Spawn&gt;: a named spawn point with a start animation ("Move|Frame").</summary>
public sealed partial class SpawnElement : PointElement
{
    public const string DefaultName = "DefaultSpawn";
    public const string DefaultAnimation = "JumpOff|18";

    public override ElementType Type => ElementType.Spawn;
    public override Size HandleSize => new(100, 100);

    [ObservableProperty] private string _animation = DefaultAnimation;

    protected override SceneElement CreateEmpty() => new SpawnElement();

    protected override void CopyTo(SceneElement target)
    {
        base.CopyTo(target);
        ((SpawnElement)target).Animation = Animation;
    }
}

public enum ModelType
{
    Physics = 0,
    Static = 1
}

/// <summary>&lt;Model&gt;: a node-based 3D model drawn as a black silhouette. Can be moved only.</summary>
public sealed partial class ModelElement : PointElement
{
    public override ElementType Type => ElementType.Model;
    public override bool NameEditable => false;
    public override bool SupportsDynamics => false;

    [ObservableProperty] private ModelType _modelType = ModelType.Static;
    [ObservableProperty] private int _lifeTime = 10;

    /// <summary>Silhouette bounds relative to the pivot, filled in by the renderer when the model loads.</summary>
    public Rect? SilhouetteBounds { get; set; }

    public string ClassName
    {
        get => Name;
        set => Name = value;
    }

    public override Point[] GetLocalCorners()
    {
        if (SilhouetteBounds is { } b)
        {
            return new[]
            {
                new Point(X + b.Left, Y + b.Top),
                new Point(X + b.Right, Y + b.Top),
                new Point(X + b.Right, Y + b.Bottom),
                new Point(X + b.Left, Y + b.Bottom),
            };
        }
        return base.GetLocalCorners();
    }

    protected override SceneElement CreateEmpty() => new ModelElement();

    protected override void CopyTo(SceneElement target)
    {
        base.CopyTo(target);
        var t = (ModelElement)target;
        t.ModelType = ModelType;
        t.LifeTime = LifeTime;
        t.SilhouetteBounds = SilhouetteBounds;
    }
}

public enum ItemType
{
    Bonus = 0,
    Coin = 1
}

/// <summary>&lt;Item&gt;: a collectible (Bonus pill or Coin).</summary>
public sealed partial class ItemElement : PointElement
{
    public override ElementType Type => ElementType.Item;
    public override bool NameEditable => false;

    [ObservableProperty] private ItemType _itemType = ItemType.Bonus;
    [ObservableProperty] private int _score = 10;
    [ObservableProperty] private double _radius = 80;
    [ObservableProperty] private int _groupId = 1;

    partial void OnItemTypeChanged(ItemType value) => Name = value == ItemType.Bonus ? "Bonus" : "Coin";

    protected override SceneElement CreateEmpty() => new ItemElement();

    protected override void CopyTo(SceneElement target)
    {
        base.CopyTo(target);
        var t = (ItemElement)target;
        t.ItemType = ItemType;
        t.Score = Score;
        t.Radius = Radius;
        t.GroupId = GroupId;
        t.Name = Name;
    }
}

/// <summary>&lt;Animation&gt;: an animated sprite that can move by itself (Type 1: direction/acceleration).</summary>
public sealed partial class AnimationElement : SceneElement
{
    public override ElementType Type => ElementType.Animation;
    public override bool NameEditable => false;

    [ObservableProperty] private int _animationType = 1;
    [ObservableProperty] private string _direction = "0|0";
    [ObservableProperty] private string _acceleration = "0|0";
    [ObservableProperty] private double _time;
    [ObservableProperty] private double _scaleX = 1;
    [ObservableProperty] private double _scaleY = 1;

    public string ClassName
    {
        get => Name;
        set => Name = value;
    }

    protected override SceneElement CreateEmpty() => new AnimationElement();

    protected override void CopyTo(SceneElement target)
    {
        base.CopyTo(target);
        var t = (AnimationElement)target;
        t.AnimationType = AnimationType;
        t.Direction = Direction;
        t.Acceleration = Acceleration;
        t.Time = Time;
        t.ScaleX = ScaleX;
        t.ScaleY = ScaleY;
    }
}

/// <summary>&lt;Particle&gt;: a particle effect sprite.</summary>
public sealed partial class ParticleElement : SceneElement
{
    public const string DefaultClassName = "p_glass1_mini";

    public override ElementType Type => ElementType.Particle;
    public override bool NameEditable => false;

    [ObservableProperty] private int _particleType = 1;
    [ObservableProperty] private int _frame;

    public string ClassName
    {
        get => Name;
        set => Name = value;
    }

    protected override SceneElement CreateEmpty() => new ParticleElement();

    protected override void CopyTo(SceneElement target)
    {
        base.CopyTo(target);
        var t = (ParticleElement)target;
        t.ParticleType = ParticleType;
        t.Frame = Frame;
    }
}

/// <summary>
/// Editor-only annotation. It is not game data: it is saved as an XML comment
/// (&lt;!--Vectorier.Comment ...--&gt;) inside its parent's Content, which the game ignores.
/// </summary>
public sealed class CommentElement : SceneElement
{
    public override ElementType Type => ElementType.Comment;
    public override bool LayerEditable => false;
    public override bool SupportsDynamics => false;

    protected override SceneElement CreateEmpty() => new CommentElement();
}

/// <summary>An XML element the editor does not understand. Written back exactly as it was read.</summary>
public sealed class UnknownElement : SceneElement
{
    public override ElementType Type => ElementType.Unknown;
    public override bool HasSize => false;
    public override bool CanResize => false;
    public override bool NameEditable => false;
    public override bool SupportsDynamics => false;

    public XElement Xml { get; set; } = new("Unknown");

    public override Point[] GetLocalCorners() => new[] { new Point(X, Y), new Point(X, Y), new Point(X, Y), new Point(X, Y) };

    protected override SceneElement CreateEmpty() => new UnknownElement();

    protected override void CopyTo(SceneElement target)
    {
        base.CopyTo(target);
        ((UnknownElement)target).Xml = new XElement(Xml);
    }
}
