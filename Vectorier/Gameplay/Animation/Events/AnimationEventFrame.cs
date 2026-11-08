#nullable disable warnings
namespace Vectorier.Gameplay.Animation.Events;

public class AnimationEventFrame : AnimationEvent
{
    public int Frame { get; }

    public AnimationEventFrame(int frame, AnimationEventParam param)
        : base(param)
    {
        Frame = frame;
    }
}
