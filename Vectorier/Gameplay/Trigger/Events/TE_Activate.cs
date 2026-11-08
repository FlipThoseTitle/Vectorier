#nullable disable warnings
namespace Vectorier.Gameplay.Trigger.Events;

public class TE_Activate : TriggerEvent
{
    public string ActionID;

    public TE_Activate(string actionID)
    {
        ActionID = actionID;
        _Type = EventType.TET_ACTIVATE;
    }
}
