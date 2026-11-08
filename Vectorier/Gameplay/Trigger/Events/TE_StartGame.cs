#nullable disable warnings
using System.Collections.Generic;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Trigger.Events;

public class TE_StartGame : TriggerEvent
{
    private static List<TriggerRunner> _withThisEvent;

    /// <summary>The editor's addition: a new game has its own start triggers.</summary>
    public static void ResetAll() => _withThisEvent = null;

    public TE_StartGame(TriggerRunner parent)
    {
        _Type = EventType.TET_ON_START_GAME;
        if (parent != null)
        {
            AddToWithThisEvent(parent);
        }
    }

    private static void AddToWithThisEvent(TriggerRunner parent)
    {
        if (_withThisEvent == null)
        {
            _withThisEvent = new List<TriggerRunner>();
        }
        if (!_withThisEvent.Contains(parent))
        {
            _withThisEvent.Add(parent);
        }
    }

    public static void ActivateThisEvent()
    {
        if (_withThisEvent == null)
        {
            return;
        }
        TE_StartGame p_event = new TE_StartGame(null);
        foreach (TriggerRunner item in _withThisEvent)
        {
            item.CheckEvent(p_event, null);
        }
    }
}
