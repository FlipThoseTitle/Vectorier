#nullable disable warnings
using System;
using System.Xml;
using Vectorier.Gameplay.Trigger.Events;

namespace Vectorier.Gameplay.Trigger;

public class TriggerEvent
{
    public enum EventType
    {
        TET_NONE,
        TET_ENTER,
        TET_EXIT,
        TET_LINE,
        TET_TIMEOUT,
        TET_KEY,
        TET_ACTIVATE,
        TET_COLISION,
        TET_VAR_CHANGE,
        TET_ON_SHOW,
        TET_ON_HIDE,
        TET_ON_START_GAME
    }

    protected EventType _Type = EventType.TET_NONE;

    protected TriggerLoop _Parent;

    public EventType Type => _Type;

    protected TriggerEvent()
    {

    }

    public static TriggerEvent Create(XmlNode node, TriggerLoop parent)
    {
        TriggerEvent triggerRunnerEvent = null;
        if (node.LocalName.Equals("Enter"))
        {
            triggerRunnerEvent = new TE_Enter();
        }
        if (node.LocalName.Equals("Exit"))
        {
            triggerRunnerEvent = new TE_Exit();
        }
        if (node.LocalName.Equals("Timeout"))
        {
            triggerRunnerEvent = new TE_Timeout();
        }
        if (node.LocalName.Equals("KeyPressed"))
        {
            triggerRunnerEvent = new TE_Key(string.Empty);
        }
        if (node.LocalName.Equals("Activate"))
        {
            triggerRunnerEvent = new TE_Activate(string.Empty);
        }
        if (node.LocalName.Equals("Line"))
        {
            triggerRunnerEvent = new TE_Line(parent, node);
        }
        if (node.LocalName.Equals("Collision"))
        {
            triggerRunnerEvent = new TE_Collision();
        }
        if (node.LocalName.Equals("OnShow"))
        {
            triggerRunnerEvent = new TE_OnShow(parent.ParentTrigger);
        }
        if (node.LocalName.Equals("OnHide"))
        {
            triggerRunnerEvent = new TE_OnHide(parent.ParentTrigger);
        }
        if (node.LocalName.Equals("ValueChange"))
        {
            triggerRunnerEvent = new TE_ChangeVar(node, parent.ParentTrigger);
        }
        if (node.LocalName.Equals("OnStartGame"))
        {
            triggerRunnerEvent = new TE_StartGame(parent.ParentTrigger);
        }
        if (triggerRunnerEvent == null)
        {
            Diagnostics.GameLog.Warning("Loading the level's triggers", $"Unknown trigger event <{node.Name}>: it is ignored.", node: node);
            return null;
        }
        triggerRunnerEvent._Parent = parent;
        return triggerRunnerEvent;
    }

    public virtual bool IsEqual(TriggerEvent value)
    {
        return value.Type == _Type;
    }

    public bool IsCollision()
    {
        return _Type == EventType.TET_COLISION;
    }

    public bool IsTimeOutOrActivate()
    {
        if (_Type == EventType.TET_TIMEOUT || _Type == EventType.TET_ACTIVATE)
        {
            return true;
        }
        return false;
    }

    public override string ToString()
    {
        return "";
    }
}
