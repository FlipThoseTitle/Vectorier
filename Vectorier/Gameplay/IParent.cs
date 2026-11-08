#nullable disable warnings
using System.Numerics;

namespace Vectorier.Gameplay;

public interface IParent
{
    Vector2 LocalPosition { get; set; }
    Vector2 LocalScale { get; set; }

    Vector2 WorldPosition { get; }
    Vector2 WorldScale { get; }

    Vector2 TransformPoint(Vector2 point);
    Vector2 TransformPoint(Vector3 point);
}
