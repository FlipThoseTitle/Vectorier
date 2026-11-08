#nullable disable warnings
using System.Collections.Generic;
namespace Vectorier.Gameplay.Animation.Events;

public class AnimationEventCollision : AnimationEvent
{
    public enum Type
    {
        Quad = 1,
        Primitive,
        PrimitiveAnimated
    }

    public List<Type> Types { get; }

    public AnimationEventCollision(List<Type> types, AnimationEventParam param)
        : base(param)
    {
        Types = types;
    }
}
