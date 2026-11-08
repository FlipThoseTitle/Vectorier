#nullable disable warnings
using Vectorier.Gameplay.Detector;

namespace Vectorier.Gameplay.Animation.Events;

public class AnimationEventDetector : AnimationEvent
{
    public DetectorEvent.DetectorEventType Type { get; }

    public AnimationEventDetector(DetectorEvent.DetectorEventType type, AnimationEventParam param)
        : base(param)
    {
        Type = type;
    }
}
