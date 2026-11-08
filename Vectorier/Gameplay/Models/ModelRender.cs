#nullable disable warnings
using System;
using System.Collections.Generic;
using Vectorier.Gameplay.Geometry;
using Vectorier.Gameplay.Level;
using Vectorier.Gameplay.Node;
using Vectorier.Gameplay.Rl;

namespace Vectorier.Gameplay.Models;

public class ModelRender : IDrawable
{
    private Color _color;

    public float Z { get; set; } = -15;

    public Color Color
    {
        get => _color;
        set => _color = value;
    }

    public string Name { get; set; }

    public bool IsEnabled { get; set; } = true;

    public List<Capsule> Capsules { get; } = new List<Capsule>();

    public List<Triangle> Triangles { get; } = new List<Triangle>();

    public ModelRender()
    {
        // In the editor, models outside a level (previews, the Move Visualizer's still poses) are not the level's.
        if (Detached)
        {
            return;
        }
        GlobalObjects.AddDrawable(this);
        Z += Sets.Current.Containers[1].Z;
    }

    public void Draw()
    {
        foreach (var capsule in Capsules)
        {
            capsule.Draw(Color);
        }
        foreach (var triangle in Triangles)
        {
            triangle.Draw(Color);
        }
    }

    /// <summary>Set while the editor builds a model that does not belong to the level being played.</summary>
    [ThreadStatic]
    public static bool Detached;

    /// <summary>The editor's drawing of the same capsules and triangles, into one geometry (model previews).</summary>
    public void Draw(ModelCanvas canvas)
    {
        foreach (var capsule in Capsules)
        {
            capsule.Draw(canvas);
        }
        foreach (var triangle in Triangles)
        {
            triangle.Draw(canvas);
        }
    }

    public void Add(List<ModelNode> Nodes)
    {
    }

    public void Add(ModelNode Node)
    {
    }

    public void Add(List<ModelLine> Lines)
    {
    }

    public void Add(ModelLine modelLine)
    {
        switch (modelLine.Type)
        {
            case "Capsule":
                var capsule = new Capsule();
                capsule.Init(modelLine);
                Capsules.Add(capsule);
                break;
        }
    }

    public void Add(List<Triangle> Triangles)
    {
        this.Triangles.AddRange(Triangles);
    }

    public void Add(Triangle Triangles)
    {
        this.Triangles.Add(Triangles);
    }

    public void Add(Model Model)
    {
    }
}
