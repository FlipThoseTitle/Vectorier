#nullable disable warnings
namespace Vectorier.Gameplay.Trigger.Events;

public class TE_Key : TriggerEvent
{
    public string Key;

    public TE_Key(string value = "")
    {
        Key = value;
        _Type = EventType.TET_KEY;
    }
}
