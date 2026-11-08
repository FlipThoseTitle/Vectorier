#nullable disable warnings
using System.Collections.Generic;
using Vectorier.Gameplay.Node;
using Vectorier.Gameplay.Runners;
using Vectorier.Gameplay.Trigger.Events;

namespace Vectorier.Gameplay.Models;

public class ControllerTrigger
{
    private List<TriggerRunner> _activeTriggers = new List<TriggerRunner>();

    private List<TriggerRunner> _lineTrggers = new List<TriggerRunner>();

    private ModelHuman _model;

    public ControllerTrigger(ModelHuman model)
    {
        _model = model;
    }

    public ModelNode GetNode(TriggerRunner trigger)
    {
        return _model.GetNode(trigger.NodeName);
    }

    public ModelNode GetNode(string trigger)
    {
        return _model.GetNode(trigger);
    }

    public void Check(TriggerRunner triger)
    {
        bool isActive = GetIsActive(triger);
        if (CheckAI(triger))
        {
            if (GetInside(triger))
            {
                if (!isActive)
                {
                    Activate(triger);
                }
            }
            else if (isActive)
            {
                DeActivate(triger);
            }
        }
        else if (isActive)
        {
            DeActivate(triger, false);
        }
    }

    public void Render()
    {
        CheckLine();
        CheckRenderEvents();
    }

    private void CheckLine()
    {
        for (int i = 0; i < _lineTrggers.Count; i++)
        {
            for (int j = 0; j < _lineTrggers[i].Lines.Count; j++)
            {
                _lineTrggers[i].Lines[j].Check(_model);
            }
        }
    }

    private void CheckRenderEvents()
    {
        for (int i = 0; i < _activeTriggers.Count; i++)
        {
            _activeTriggers[i].CheckRenderEvent(_model);
        }
    }

    private void Activate(TriggerRunner trigger)
    {
        trigger.CheckEvent(new TE_Enter(), _model);
        trigger.CheckEvent(new TE_Collision(), _model);
        _activeTriggers.Add(trigger);
        if (trigger.Lines != null)
        {
            _lineTrggers.Add(trigger);
        }

    }

    private void DeActivate(TriggerRunner triger, bool sendEvent = true)
    {
        if (sendEvent)
        {
            TE_Exit p_event = new TE_Exit();
            triger.CheckEvent(p_event, _model);
        }
        _activeTriggers.Remove(triger);
        if (triger.Lines != null)
        {
            _lineTrggers.Remove(triger);
        }
        triger.ResetRenderEvents();
        _model.CheckDelayAction(triger);
    }

    public void SetKeyEvent(string key)
    {
        TE_Key p_event = new TE_Key(key);
        for (int i = 0; i < _activeTriggers.Count; i++)
        {
            _activeTriggers[i].CheckEvent(p_event, _model);
        }
    }

    private bool GetIsActive(TriggerRunner triger)
    {
        return _activeTriggers.Contains(triger);
    }

    private bool CheckAI(TriggerRunner triger)
    {
        return _model.AI == triger.AIVar.ValueInt || triger.AIVar.ValueInt == -1;
    }

    public bool GetInside(TriggerRunner trigger)
    {
        switch (trigger.CollisionType)
        {
            case TriggerRunner.TriggerColisionType.OneNode:
                return trigger.Hit(GetNode(trigger).Start);
            case TriggerRunner.TriggerColisionType.MultiNode:
                {
                    string text = null;
                    for (int i = 0; i < trigger.TriggerNodesName.Count; i++)
                    {
                        text = trigger.CollisionNodeName = trigger.TriggerNodesName[i];
                        if (trigger.Hit(GetNode(text).Start))
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
        _activeTriggers.Clear();
        _lineTrggers.Clear();
    }
}
