#nullable disable warnings
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Trigger.Events;

public class TE_OnHide : TriggerEvent
{
    public TE_OnHide()
    {
        _Type = EventType.TET_ON_HIDE;
    }

    public TE_OnHide(TriggerRunner parent)
    {
        _Type = EventType.TET_ON_HIDE;
    }
}
