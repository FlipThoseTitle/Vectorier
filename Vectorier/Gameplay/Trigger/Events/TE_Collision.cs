#nullable disable warnings
namespace Vectorier.Gameplay.Trigger.Events;

public class TE_Collision : TriggerEvent
{
    public TE_Collision()
    {
        _Type = EventType.TET_COLISION;
    }
}
