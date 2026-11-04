// Ported from Vector-Raylib-For-Vectorier (the game's player movement), see Gameplay/README.md.
// Only what the movement uses: the position (level coordinates, Y down) and how far it moved this tick. Dynamic
// transformations (moving platforms) run from triggers in the game, which are not part of the editor's gameplay.
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System.Numerics;

namespace Vectorier.Gameplay.Runners
{
    public abstract class Runner : IDrawable
    {
        public string Name;

        public int Hash;

        public float Z { get; set; }

        public Vector2 LocalPosition { get; set; } = Vector2.Zero;

        public Vector2 WorldPosition => LocalPosition;

        public Vector2 DefaultPosition = Vector2.Zero;

        /// <summary>How far the runner moved this tick (a moving platform carries the model by it).</summary>
        public Vector2 TweenPosition;

        public bool IsEnabled = true;

        public RunnerType TypeClass;

        public int ActiveTransformation;

        public Runner(float x, float y)
        {
            LocalPosition = new Vector2(x, y);
            DefaultPosition = new Vector2(x, y);
        }

        public virtual void InitRunner(Vector2 point)
        {
            Move(point);
            DefaultPosition = LocalPosition;
        }

        public virtual void Move(Vector2 point)
        {
            LocalPosition += point;
        }

        public virtual void UpdatePosition(Vector2 point)
        {
            TweenPosition = point;
            Move(point);
        }

        public virtual void ResetPosition()
        {
            TweenPosition = Vector2.Zero;
        }

        public virtual void Reset()
        {
            ResetPosition();
            LocalPosition = DefaultPosition;
            ActiveTransformation = 0;
        }

        public virtual bool Render()
        {
            return true;
        }

        public virtual void Draw()
        {
        }

        public virtual void TransformationStart()
        {
            ActiveTransformation++;
        }

        public virtual void TransformationEnd()
        {
            ActiveTransformation--;
        }

        public virtual void TransformResize(Vector2 size)
        {
        }

        public virtual void TransformRotate(float angle)
        {
        }
    }
}
