using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Vectorier.Models.Scene;

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
