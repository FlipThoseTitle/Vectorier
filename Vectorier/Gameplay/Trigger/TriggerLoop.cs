// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Vectorier.Gameplay.Runners;
using Vectorier.Gameplay.Trigger.Events;
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Trigger
{
    public class TriggerLoop
    {
        private bool _IsRender;

        private string _Name;

        private TriggerRunner _Parent;

        private List<TriggerAction> _Actions = new List<TriggerAction>();

        private List<TriggerEvent> _Events = new List<TriggerEvent>();

        private List<TriggerCondition> _Conditions = new List<TriggerCondition>();

        public TriggerRunner ParentTrigger => _Parent;

        public string Name => _Name;

        public List<TriggerAction> Actions => _Actions;

        public List<TriggerEvent> Events => _Events;

        private TriggerLoop(XmlNode p_node, TriggerRunner p_parent)
        {
            _Name = p_node.Attributes["Name"].ParseString(string.Empty);
            _IsRender = true;
            _Parent = p_parent;
            ParseEvent(p_node["Events"]);
            TriggerCondition.ParseConditions(p_node["Conditions"], this, _Conditions);
            TriggerAction.ParseActions(p_node["Actions"], this, _Actions);
        }

        public static TriggerLoop createLoop(XmlNode p_node, TriggerRunner p_parent)
        {
            return new TriggerLoop(p_node, p_parent);
        }

        private void ParseEvent(XmlNode p_node)
        {
            if (p_node == null)
            {
                return;
            }
            XmlAttribute xmlAttribute = p_node.Attributes["Template"];
            if (xmlAttribute != null)
            {
                ParseEvent(TemplateModule.getTemplateEventsXml(xmlAttribute.Value));
                return;
            }
            foreach (XmlNode childNode in p_node.ChildNodes)
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
                    _Events.Add(triggerRunnerEvent);
                }
            }
        }

        private bool CheckConditions()
        {
            for (int i = 0; i < _Conditions.Count; i++)
            {
                if (!_Conditions[i].Check())
                {
                    return false;
                }
            }
            return true;
        }

        private void ExtraActionsOnEvent(TriggerEvent p_Event)
        {
            _Parent.SetModelVar();
            switch (p_Event.Type)
            {
                case TriggerEvent.EventType.TET_KEY:
                    _Parent.SetKeyVar((p_Event as TE_Key).Key);
                    break;
                case TriggerEvent.EventType.TET_ACTIVATE:
                    _Parent.GetVar("_$ActionID").setValue((p_Event as TE_Activate).ActionID);
                    break;
            }
        }

        public void SetLine(string p_type, Variable p_value, int p_iD)
        {
            TriggerLine triggerLine = new TriggerLine(_Parent, p_iD);
            if (p_type[0] == '_')
            {
                p_type = ParentTrigger.GetVar(p_type).ValueString;
            }
            triggerLine.SetLine(p_type, p_value);
            _Parent.AddLine(triggerLine);
        }

        public void Reset()
        {
            foreach (var action in _Actions)
            {
                action.Reset();
            }
        }

        public bool CheckEvent(TriggerEvent p_event)
        {
            for (int i = 0; i < _Events.Count; i++)
            {
                if (_Events[i].IsEqual(p_event))
                {
                    ExtraActionsOnEvent(p_event);
                    if (CheckConditions())
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public Variable GetParentVar(string p_key)
        {
            return _Parent.GetVar(p_key);
        }

        public override string ToString()
        {
            string text = "Name:" + Name;
            text += "\n Events:";
            foreach (TriggerEvent @event in _Events)
            {
                text = text + "\n   " + @event;
            }
            text += "\n Conditions:";
            foreach (TriggerCondition condition in _Conditions)
            {
                text = text + "\n   " + condition;
            }
            text += "\n Actions:";
            foreach (TriggerAction action in _Actions)
            {
                text = text + "\n   " + action;
            }
            return text;
        }
    }
}
