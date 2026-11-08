#nullable disable warnings
namespace Vectorier.Gameplay.Trigger.Events;

public class TE_Timeout : TriggerEvent
{
    public TE_Timeout()
    {
        _Type = EventType.TET_TIMEOUT;
    }
}
