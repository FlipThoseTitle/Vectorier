// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vectorier.Gameplay.Node;
using Vectorier.Gameplay.Runners;
using Vectorier.Gameplay.Trigger.Events;

namespace Vectorier.Gameplay.Models
{
    public class ControllerTrigger
    {
        private List<TriggerRunner> _ActiveTriggers = new List<TriggerRunner>();

        private List<TriggerRunner> _LineTrggers = new List<TriggerRunner>();

        private ModelHuman _Model;

        private ModelNode _ComNode;

        public ControllerTrigger(ModelHuman model)
        {
            _Model = model;
        }

        public ModelNode GetNode(TriggerRunner p_trigger)
        {
            return _Model.GetNode(p_trigger.NodeName);
        }

        public ModelNode GetNode(string p_trigger)
        {
            return _Model.GetNode(p_trigger);
        }

        public void Check(TriggerRunner p_triger)
        {
            bool isActive = GetIsActive(p_triger);
            if (CheckAI(p_triger))
            {
                if (GetInside(p_triger))
                {
                    if (!isActive)
                    {
                        Activate(p_triger);
                    }
                }
                else if (isActive)
                {
                    DeActivate(p_triger);
                }
            }
            else if (isActive)
            {
                DeActivate(p_triger, false);
            }
        }

        public void Render()
        {
            CheckLine();
            CheckRenderEvents();
        }

        private void CheckLine()
        {
            for (int i = 0; i < _LineTrggers.Count; i++)
            {
                for (int j = 0; j < _LineTrggers[i].Lines.Count; j++)
                {
                    _LineTrggers[i].Lines[j].Check(_Model);
                }
            }
        }

        private void CheckRenderEvents()
        {
            for (int i = 0; i < _ActiveTriggers.Count; i++)
            {
                _ActiveTriggers[i].CheckRenderEvent(_Model);
            }
        }

        private void Activate(TriggerRunner p_trigger)
        {
            p_trigger.CheckEvent(new TE_Enter(), _Model);
            p_trigger.CheckEvent(new TE_Collision(), _Model);
            _ActiveTriggers.Add(p_trigger);
            if (p_trigger.Lines != null)
            {
                _LineTrggers.Add(p_trigger);
            }

        }

        private void DeActivate(TriggerRunner p_triger, bool p_sendEvent = true)
        {
            if (p_sendEvent)
            {
                TE_Exit p_event = new TE_Exit();
                p_triger.CheckEvent(p_event, _Model);
            }
            _ActiveTriggers.Remove(p_triger);
            if (p_triger.Lines != null)
            {
                _LineTrggers.Remove(p_triger);
            }
            p_triger.ResetRenderEvents();
            _Model.CheckDelayAction(p_triger);
        }

        public void SetKeyEvent(string p_key)
        {
            TE_Key p_event = new TE_Key(p_key);
            for (int i = 0; i < _ActiveTriggers.Count; i++)
            {
                _ActiveTriggers[i].CheckEvent(p_event, _Model);
            }
        }

        private bool GetIsActive(TriggerRunner p_triger)
        {
            return _ActiveTriggers.Contains(p_triger);
        }

        private bool CheckAI(TriggerRunner p_triger)
        {
            return _Model.AI == p_triger.AIVar.ValueInt || p_triger.AIVar.ValueInt == -1;
        }

        public bool GetInside(TriggerRunner p_trigger)
        {
            switch (p_trigger.CollisionType)
            {
                case TriggerRunner.TriggerColisionType.OneNode:
                    return p_trigger.Hit(GetNode(p_trigger).Start);
                case TriggerRunner.TriggerColisionType.MultiNode:
                    {
                        string text = null;
                        for (int i = 0; i < p_trigger.TriggerNodesName.Count; i++)
                        {
                            text = p_trigger.CollisionNodeName = p_trigger.TriggerNodesName[i];
                            if (p_trigger.Hit(GetNode(text).Start))
                            {
                                return true;
                            }
                        }
                        return false;
                    }
                default:
                    return false;
            }
        }

        public void Reset()
        {
            _ActiveTriggers.Clear();
            _LineTrggers.Clear();
        }
    }
}
