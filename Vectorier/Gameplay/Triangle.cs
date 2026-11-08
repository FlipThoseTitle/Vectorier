#nullable disable warnings
using System.Numerics;
using Vectorier.Gameplay.Level;
using Vectorier.Gameplay.Node;
using Vectorier.Gameplay.Rl;

namespace Vectorier.Gameplay;

public class Triangle
{
    public ModelNode Node0;

    public ModelNode Node1;

    public ModelNode Node2;

    public bool IsVisible;

    public void Draw(Color color)
    {
        if (!IsVisible) return;

        var vs = Sets.Current.Containers[1];

        Vector2 a = vs.TransformPoint(Node0.Start);
        Vector2 b = vs.TransformPoint(Node1.Start);
        Vector2 c = vs.TransformPoint(Node2.Start);

        Rlgl.Begin(DrawMode.Triangles);

        Rlgl.Color4ub(color.R, color.G, color.B, color.A);

        Rlgl.Vertex2f(a.X, a.Y);
        Rlgl.Vertex2f(b.X, b.Y);
        Rlgl.Vertex2f(c.X, c.Y);

        Rlgl.End();
    }

    /// <summary>The game's Triangle.Draw: the three nodes filled (both faces).</summary>
    public void Draw(Models.ModelCanvas canvas)
    {
        if (!IsVisible) return;

        var a = canvas.Transform(Node0.Start);
        var b = canvas.Transform(Node1.Start);
        var c = canvas.Transform(Node2.Start);

        canvas.Polygon(a, b, c);
    }
}
