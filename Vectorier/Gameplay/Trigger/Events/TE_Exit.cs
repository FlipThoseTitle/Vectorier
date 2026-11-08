#nullable disable warnings
namespace Vectorier.Gameplay.Trigger.Events;

public class TE_Exit : TriggerEvent
{
    public TE_Exit()
    {
        _Type = EventType.TET_EXIT;
    }
}
