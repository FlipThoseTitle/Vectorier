#nullable disable warnings
using System.Collections.Generic;
using System.Xml;
using Vectorier.Gameplay.Runners;
using Vectorier.Gameplay.Trigger.Events;
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Trigger;

public class TriggerLoop
{
    private List<TriggerCondition> _conditions = new List<TriggerCondition>();

    public TriggerRunner ParentTrigger { get; }

    public string Name { get; }

    public List<TriggerAction> Actions { get; } = new List<TriggerAction>();

    public List<TriggerEvent> Events { get; } = new List<TriggerEvent>();

    private TriggerLoop(XmlNode node, TriggerRunner parent)
    {
        Name = node.Attributes["Name"].ParseString(string.Empty);
        ParentTrigger = parent;
        ParseEvent(node["Events"]);
        TriggerCondition.ParseConditions(node["Conditions"], this, _conditions);
        TriggerAction.ParseActions(node["Actions"], this, Actions);
    }

    public static TriggerLoop createLoop(XmlNode node, TriggerRunner parent)
    {
        return new TriggerLoop(node, parent);
    }

    private void ParseEvent(XmlNode node)
    {
        if (node == null)
        {
            return;
        }
        XmlAttribute xmlAttribute = node.Attributes["Template"];
        if (xmlAttribute != null)
        {
            ParseEvent(TemplateModule.getTemplateEventsXml(xmlAttribute.Value));
            return;
        }
        foreach (XmlNode childNode in node.ChildNodes)
        {
            if (childNode.LocalName.Equals("EventBlock"))
            {
                string value = childNode.Attributes["Template"].Value;
                ParseEvent(TemplateModule.getTemplateEventsXml(value));
                continue;
            }
            TriggerEvent triggerRunnerEvent = TriggerEvent.Create(childNode, this);
            if (triggerRunnerEvent != null)
            {
                Events.Add(triggerRunnerEvent);
            }
        }
    }

    private bool CheckConditions()
    {
        for (int i = 0; i < _conditions.Count; i++)
        {
            if (!_conditions[i].Check())
            {
                return false;
            }
        }
        return true;
    }

    private void ExtraActionsOnEvent(TriggerEvent @event)
    {
        ParentTrigger.SetModelVar();
        switch (@event.Type)
        {
            case TriggerEvent.EventType.TET_KEY:
                ParentTrigger.SetKeyVar((@event as TE_Key).Key);
                break;
            case TriggerEvent.EventType.TET_ACTIVATE:
                ParentTrigger.GetVar("_$ActionID").setValue((@event as TE_Activate).ActionID);
                break;
        }
    }

    public void SetLine(string type, Variable value, int iD)
    {
        TriggerLine triggerLine = new TriggerLine(ParentTrigger, iD);
        if (type[0] == '_')
        {
            type = ParentTrigger.GetVar(type).ValueString;
        }
        triggerLine.SetLine(type, value);
        ParentTrigger.AddLine(triggerLine);
    }

    public void Reset()
    {
        foreach (var action in Actions)
        {
            action.Reset();
        }
    }

    public bool CheckEvent(TriggerEvent @event)
    {
        for (int i = 0; i < Events.Count; i++)
        {
            if (Events[i].IsEqual(@event))
            {
                ExtraActionsOnEvent(@event);
                if (CheckConditions())
                {
                    return true;
                }
            }
        }
        return false;
    }

    public Variable GetParentVar(string key)
    {
        return ParentTrigger.GetVar(key);
    }

    public override string ToString()
    {
        string text = "Name:" + Name;
        text += "\n Events:";
        foreach (TriggerEvent @event in Events)
        {
            text = text + "\n   " + @event;
        }
        text += "\n Conditions:";
        foreach (TriggerCondition condition in _conditions)
        {
            text = text + "\n   " + condition;
        }
        text += "\n Actions:";
        foreach (TriggerAction action in Actions)
        {
            text = text + "\n   " + action;
        }
        return text;
    }
}
