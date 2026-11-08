#nullable disable warnings
using System.Numerics;
using Vectorier.Gameplay.Rl;
using Vectorier.Gameplay.Runners.Animation;
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Runners;

public class VisualRunner : Runner
{
    public int Type;

    public int Depth;

    public static int SortingOrder;

    public int Order;

    public bool IsVanishing => Type == 2;

    public bool IsAnimation;

    public float Width;

    public float Height;

    public float DefaultWidth;

    public float DefaultHeight;

    public Color Color;

    public Color DefaultColor;

    public Matrix? Matrix;

    public ImageRendering Image;

    public AnimationSprite Animation;

    public VisualRunner(string className, float x, float y, float width, float height, int type, int depth, Matrix? matrix = null, Color color = default) : base(x, y)
    {
        Name = className;

        TypeClass = RunnerType.Visual;

        Type = type;
        Depth = depth;

        Order = SortingOrder;
        SortingOrder++;

        Z = Order * -0.0001f - 1;

        Width = width;
        Height = height;
        DefaultWidth = width;
        DefaultHeight = height;

        Color = color;
        DefaultColor = color;

        Matrix = matrix;




        IsAnimation = ResourceManager.FileExists(VectorPaths.AnimatedTextures + "/" + Name, out string path, ".plist", ".json");
    }

    public override void InitRunner(Vector2 point)
    {
        base.InitRunner(point);

        if (Depth == 1)
        {
            Z += 3;
        }
        if (Depth == 0)
        {
            Z -= 3;
        }
    }

    public void Init()
    {
        if (IsAnimation)
        {
            Animation = new AnimationSprite(Name, Width, Height)
            {
                Parent = this,
                Iterations = -1,
                FPS = 20,
                Matrix = Matrix,
                Z = Z
            };
            Animation.Play();
            return;
        }

        Image = new ImageRendering(Name, 0, 0, Width, Height, Matrix, Color)
        {
            Parent = this,
            Z = Z
        };
        Image.GenerateContent();
    }

    public override void TransformColor(Vectorier.Gameplay.Rl.Color delta)
    {

        Color.Add(delta);
        UpdateColor();
    }

    public override void TransformColorEnd(Vectorier.Gameplay.Rl.Color color)
    {
        Color = color;
        UpdateColor();
    }

    public void UpdateColor()
    {
        if (Color.R < 0f)
        {
            return;
        }
        if (Image != null)
        {
            Image.Color = Color;
        }
    }
}
