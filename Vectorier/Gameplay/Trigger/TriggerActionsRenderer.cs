// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace Vectorier.Gameplay.Trigger;

public class TriggerActionsRenderer
{
    private static TriggerActionsRenderer _Current;

    private readonly Queue<List<TriggerAction>> _PendingActions = new Queue<List<TriggerAction>>();
    private readonly List<List<TriggerAction>> _DelayActions = new List<List<TriggerAction>>();

    private bool _IsProcessing;
    private int _ActionsThisFrame;
    private const int MaxActionsPerFrame = 1000;

    public static TriggerActionsRenderer Current
    {
        get
        {
            if (_Current == null)
            {
                _Current = new TriggerActionsRenderer();
            }

            return _Current;
        }
    }

    public static void Reset()
    {
        Current._PendingActions.Clear();
        Current._DelayActions.Clear();
        Current._IsProcessing = false;
        Current._ActionsThisFrame = 0;
    }

    public void AddActions(List<TriggerAction> p_actions)
    {
        if (p_actions == null || p_actions.Count == 0)
        {
            return;
        }

        _PendingActions.Enqueue(p_actions);

        if (!_IsProcessing)
        {
            ProcessPendingActions();
        }
    }

    private void ProcessPendingActions()
    {
        _IsProcessing = true;

        try
        {
            while (_PendingActions.Count > 0)
            {
                if (_ActionsThisFrame++ > MaxActionsPerFrame)
                {
                    break;
                }

                List<TriggerAction> actions = _PendingActions.Dequeue();
                RunActions(actions);
            }
        }
        finally
        {
            _IsProcessing = false;
        }
    }

    private void RunActions(List<TriggerAction> p_actions)
    {
        int count = p_actions.Count;

        for (int i = 0; i < count; i++)
        {
            bool isRunNext = true;
            p_actions[i].Activate(ref isRunNext);

            if (!isRunNext)
            {
                CopyActionToDelay(p_actions, i, count);
                break;
            }
        }
    }

    public void CopyActionToDelay(List<TriggerAction> p_actions, int p_from, int size)
    {
        List<TriggerAction> list = new List<TriggerAction>(size - p_from);

        for (int num = size - 1; num >= p_from; num--)
        {
            list.Add(p_actions[num]);
        }

        _DelayActions.Add(list);
    }

    public void Render()
    {
        _ActionsThisFrame = 0;

        ProcessDelayActions();
        ProcessPendingActions();
    }

    private void ProcessDelayActions()
    {
        if (_DelayActions.Count == 0)
        {
            return;
        }

        for (int i = 0; i < _DelayActions.Count; i++)
        {
            List<TriggerAction> list = _DelayActions[i];
            bool isRunNext = false;

            while (list.Count != 0)
            {
                if (_ActionsThisFrame++ > MaxActionsPerFrame)
                {
                    return;
                }

                TriggerAction action = list[list.Count - 1];
                action.Activate(ref isRunNext);

                if (isRunNext)
                {
                    list.RemoveAt(list.Count - 1);
                    continue;
                }

                break;
            }
        }

        for (int i = _DelayActions.Count - 1; i >= 0; i--)
        {
            if (_DelayActions[i].Count == 0)
            {
                _DelayActions.RemoveAt(i);
            }
        }
    }
}