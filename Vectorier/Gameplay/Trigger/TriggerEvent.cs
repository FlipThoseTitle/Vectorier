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
using Vectorier.Gameplay.Trigger.Events;

namespace Vectorier.Gameplay.Trigger
{
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

        public static TriggerEvent Create(XmlNode p_node, TriggerLoop p_parent)
        {
            TriggerEvent triggerRunnerEvent = null;
            if (p_node.LocalName.Equals("Enter"))
            {
                triggerRunnerEvent = new TE_Enter();
            }
            if (p_node.LocalName.Equals("Exit"))
            {
                triggerRunnerEvent = new TE_Exit();
            }
            if (p_node.LocalName.Equals("Timeout"))
            {
                triggerRunnerEvent = new TE_Timeout();
            }
            if (p_node.LocalName.Equals("KeyPressed"))
            {
                triggerRunnerEvent = new TE_Key(string.Empty);
            }
            if (p_node.LocalName.Equals("Activate"))
            {
                triggerRunnerEvent = new TE_Activate(string.Empty);
            }
            if (p_node.LocalName.Equals("Line"))
            {
                triggerRunnerEvent = new TE_Line(p_parent, p_node);
            }
            if (p_node.LocalName.Equals("Collision"))
            {
                triggerRunnerEvent = new TE_Collision();
            }
            if (p_node.LocalName.Equals("OnShow"))
            {
                triggerRunnerEvent = new TE_OnShow(p_parent.ParentTrigger);
            }
            if (p_node.LocalName.Equals("OnHide"))
            {
                triggerRunnerEvent = new TE_OnHide(p_parent.ParentTrigger);
            }
            if (p_node.LocalName.Equals("ValueChange"))
            {
                triggerRunnerEvent = new TE_ChangeVar(p_node, p_parent.ParentTrigger);
            }
            if (p_node.LocalName.Equals("OnStartGame"))
            {
                triggerRunnerEvent = new TE_StartGame(p_parent.ParentTrigger);
            }
            if (triggerRunnerEvent == null)
            {
                Console.WriteLine("No Event type =" + p_node.Name);
                return null;
            }
            triggerRunnerEvent._Parent = p_parent;
            return triggerRunnerEvent;
        }

        public virtual bool IsEqual(TriggerEvent p_value)
        {
            return p_value.Type == _Type;
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
}
