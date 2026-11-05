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

namespace Vectorier.Gameplay.Runners
{
    public class ImageRendering : IDrawable
    {
        public float Z { get; set; }

        public IParent Parent;

        public Vector2 LocalPosition;

        public Vector2 WorldPosition
        {
            get
            {
                if (Parent == null)
                {
                    return LocalPosition;
                }
                return Parent.WorldPosition + (LocalPosition * Parent.WorldScale);
            }
        }

        public Vector2 LocalScale = Vector2.One;

        public Vector2 WorldScale
        {
            get
            {
                if (Parent == null)
                {
                    return LocalScale;
                }
                return LocalScale * Parent.WorldScale;
            }
        }

        public Vectorier.Gameplay.Rl.Rectangle SourceRectangle;

        public string Name;

        public float? Width;

        public float? Height;

        public Color Color;

        public Matrix? Matrix;

        public Texture2D Texture;

        public bool CanDraw = false;

        public Vector2 Pivot = Vector2.Zero;

        public ImageRendering(string name, float x = 0, float y = 0, float? width = null, float? height = null, Matrix? matrix = null, Color color = default, Vector2? pivot = null)
        {
            if (color == default)
            {
                color = Color.White;
            }

            Name = name;
            Width = width;
            Height = height;

            LocalPosition = new Vector2(x, y);

            Color = color;

            Matrix = matrix;

            if (pivot != null)
            {
                Pivot = pivot.Value;
            }
        }

        public void GenerateContent()
        {
            Texture = ResourceManager.GetTexture(Name);
            SourceRectangle = new Rectangle(0, 0, Texture.Width, Texture.Height);

            if (Width == null)
            {
                Width = Texture.Width;
            }
            if (Height == null)
            {
                Height = Texture.Height;
            }

            CanDraw = true;

            GlobalObjects.AddDrawable(this);
        }

        public void Draw()
        {
            if (!CanDraw ||
                (Parent != null && Parent is Runner runner && !runner.IsEnabled) ||
                !VectorGame.Instance.ImagesVisible)
            {
                return;
            }

            if (Matrix != null)
            {
                // Culled with its real (transformed) corners inside DrawTextureAffine: the plain rectangle below
                // ignores the matrix (rotation, skew, flip).
                DrawTextureAffine(Texture, WorldPosition, WorldScale, Matrix.Value, Color, Pivot);
                return;
            }

            Rectangle renderRect = GetRenderRectangle();

            if (!IsRectangleVisible(renderRect))
            {
                return;
            }

            Vectorier.Gameplay.Rl.Rectangle destination = new Vectorier.Gameplay.Rl.Rectangle(
                WorldPosition.X,
                WorldPosition.Y,
                Width.GetValueOrDefault(Texture.Width) * WorldScale.X,
                Height.GetValueOrDefault(Texture.Height) * WorldScale.Y
            );

            Vector2 localPivot = new Vector2(
                Pivot.X * destination.Width,
                Pivot.Y * destination.Height
            );

            Raylib.DrawTexturePro(Texture, SourceRectangle, destination, localPivot, 0, Color);
        }

        public Rectangle GetRenderRectangle()
        {
            //if (Matrix != null)
            //{
            //    return GetAffineRenderRectangle();
            //}

            float width = Width.GetValueOrDefault(Texture.Width) * WorldScale.X;
            float height = Height.GetValueOrDefault(Texture.Height) * WorldScale.Y;

            Vector2 localPivot = new Vector2(
                Pivot.X * width,
                Pivot.Y * height
            );

            return new Rectangle(
                WorldPosition.X - localPivot.X,
                WorldPosition.Y - localPivot.Y,
                width,
                height
            );
        }

        public Rectangle GetAffineRenderRectangle()
        {
            float w = Texture.Width;
            float h = Texture.Height;

            Matrix matrix = Matrix.Value;

            matrix.A /= w;
            matrix.B /= w;
            matrix.C /= h;
            matrix.D /= h;

            Vector2 localPivot = new Vector2(
                Pivot.X * w,
                Pivot.Y * h
            );

            Vector2 p0 = matrix.TransformPoint(-localPivot.X, -localPivot.Y);
            Vector2 p1 = matrix.TransformPoint(w - localPivot.X, -localPivot.Y);
            Vector2 p2 = matrix.TransformPoint(w - localPivot.X, h - localPivot.Y);
            Vector2 p3 = matrix.TransformPoint(-localPivot.X, h - localPivot.Y);

            p0 *= WorldScale;
            p1 *= WorldScale;
            p2 *= WorldScale;
            p3 *= WorldScale;

            p0 += WorldPosition;
            p1 += WorldPosition;
            p2 += WorldPosition;
            p3 += WorldPosition;

            float minX = MathF.Min(MathF.Min(p0.X, p1.X), MathF.Min(p2.X, p3.X));
            float minY = MathF.Min(MathF.Min(p0.Y, p1.Y), MathF.Min(p2.Y, p3.Y));
            float maxX = MathF.Max(MathF.Max(p0.X, p1.X), MathF.Max(p2.X, p3.X));
            float maxY = MathF.Max(MathF.Max(p0.Y, p1.Y), MathF.Max(p2.Y, p3.Y));

            return new Rectangle(
                minX,
                minY,
                maxX - minX,
                maxY - minY
            );
        }

        public static bool IsRectangleVisible(Rectangle rect)
        {
            return Raylib.CheckCollisionRecs(rect, VectorGame.Instance.CameraRectangle);
        }

        /// <summary>True when the bounding box of the four drawn corners overlaps the camera's view.</summary>
        public static bool IsQuadVisible(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
        {
            var view = VectorGame.Instance.CameraRectangle;
            float minX = MathF.Min(MathF.Min(p0.X, p1.X), MathF.Min(p2.X, p3.X));
            float maxX = MathF.Max(MathF.Max(p0.X, p1.X), MathF.Max(p2.X, p3.X));
            float minY = MathF.Min(MathF.Min(p0.Y, p1.Y), MathF.Min(p2.Y, p3.Y));
            float maxY = MathF.Max(MathF.Max(p0.Y, p1.Y), MathF.Max(p2.Y, p3.Y));
            return maxX >= view.MinX && minX <= view.MaxX && maxY >= view.MinY && minY <= view.MaxY;
        }

        static void DrawTextureAffine(Texture2D texture,
            Vector2 worldPosition,
            Vector2 worldScale,
    Matrix matrix,
    Color tint,
    Vector2 pivot)
        {
            float w = texture.Width;
            float h = texture.Height;

            matrix.A = matrix.A / w;
            matrix.B = matrix.B / w;
            matrix.C = matrix.C / h;
            matrix.D = matrix.D / h;

            Vector2 localPivot = new Vector2(
                pivot.X * w,
                pivot.Y * h
            );

            Vector2 p0 = matrix.TransformPoint(-localPivot.X, -localPivot.Y);
            Vector2 p1 = matrix.TransformPoint(w - localPivot.X, -localPivot.Y);
            Vector2 p2 = matrix.TransformPoint(w - localPivot.X, h - localPivot.Y);
            Vector2 p3 = matrix.TransformPoint(-localPivot.X, h - localPivot.Y);

            p0 *= worldScale;
            p1 *= worldScale;
            p2 *= worldScale;
            p3 *= worldScale;

            p0 += worldPosition;
            p1 += worldPosition;
            p2 += worldPosition;
            p3 += worldPosition;

            // Off camera (corners as actually drawn, rotation and skew included): nothing to draw.
            if (!IsQuadVisible(p0, p1, p2, p3))
            {
                return;
            }

            Rlgl.DisableBackfaceCulling();

            Rlgl.SetTexture(texture.Id);
            Rlgl.Begin(DrawMode.Quads);

            Rlgl.Color4ub(tint.R, tint.G, tint.B, tint.A);

            Rlgl.TexCoord2f(0, 0);
            Rlgl.Vertex2f(p0.X, p0.Y);

            Rlgl.TexCoord2f(1, 0);
            Rlgl.Vertex2f(p1.X, p1.Y);

            Rlgl.TexCoord2f(1, 1);
            Rlgl.Vertex2f(p2.X, p2.Y);

            Rlgl.TexCoord2f(0, 1);
            Rlgl.Vertex2f(p3.X, p3.Y);

            Rlgl.End();
            Rlgl.SetTexture(0);
        }
    }
}
