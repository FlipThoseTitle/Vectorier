// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vectorier.Gameplay.Rl;
using System.Numerics;
using Vectorier.Gameplay.Runners.Animation;
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Runners
{
    public class VisualRunner : Runner
    {
        public int Type;

        public int Depth;

        public static int SortingOrder;

        public int Order;

        public bool IsStatic => Type < 2;

        public bool IsDynamic => Type == 3;

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

        public override void TransformColor(Vectorier.Gameplay.Rl.Color p_delta)
        {

            Color.Add(p_delta);
            UpdateColor();
        }

        public override void TransformColorEnd(Vectorier.Gameplay.Rl.Color p_color)
        {
            Color = p_color;
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
}
