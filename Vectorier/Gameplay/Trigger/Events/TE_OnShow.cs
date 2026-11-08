#nullable disable warnings
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Trigger.Events;

public class TE_OnShow : TriggerEvent
{
    public TE_OnShow()
    {
        _Type = EventType.TET_ON_SHOW;
    }

    public TE_OnShow(TriggerRunner parent)
    {
        _Type = EventType.TET_ON_SHOW;
    }
}
