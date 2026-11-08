#nullable disable warnings
using System.Collections.Generic;
namespace Vectorier.Gameplay.Trigger;

public class TriggerActionsRenderer
{
    private readonly Queue<List<TriggerAction>> _pendingActions = new Queue<List<TriggerAction>>();
    private readonly List<List<TriggerAction>> _delayActions = new List<List<TriggerAction>>();

    private bool _isProcessing;
    private int _actionsThisFrame;
    private const int MaxActionsPerFrame = 1000;

    public static TriggerActionsRenderer Current
    {
        get
        {
            if (field == null)
            {
                field = new TriggerActionsRenderer();
            }

            return field;
        }
    }

    public static void Reset()
    {
        Current._pendingActions.Clear();
        Current._delayActions.Clear();
        Current._isProcessing = false;
        Current._actionsThisFrame = 0;
    }

    public void AddActions(List<TriggerAction> actions)
    {
        if (actions == null || actions.Count == 0)
        {
            return;
        }

        _pendingActions.Enqueue(actions);

        if (!_isProcessing)
        {
            ProcessPendingActions();
        }
    }

    private void ProcessPendingActions()
    {
        _isProcessing = true;

        try
        {
            while (_pendingActions.Count > 0)
            {
                if (_actionsThisFrame++ > MaxActionsPerFrame)
                {
                    break;
                }

                List<TriggerAction> actions = _pendingActions.Dequeue();
                RunActions(actions);
            }
        }
        finally
        {
            _isProcessing = false;
        }
    }

    private void RunActions(List<TriggerAction> actions)
    {
        int count = actions.Count;

        for (int i = 0; i < count; i++)
        {
            bool isRunNext = true;
            actions[i].Activate(ref isRunNext);

            if (!isRunNext)
            {
                CopyActionToDelay(actions, i, count);
                break;
            }
        }
    }

    public void CopyActionToDelay(List<TriggerAction> actions, int from, int size)
    {
        List<TriggerAction> list = new List<TriggerAction>(size - from);

        for (int num = size - 1; num >= from; num--)
        {
            list.Add(actions[num]);
        }

        _delayActions.Add(list);
    }

    public void Render()
    {
        _actionsThisFrame = 0;

        ProcessDelayActions();
        ProcessPendingActions();
    }

    private void ProcessDelayActions()
    {
        if (_delayActions.Count == 0)
        {
            return;
        }

        for (int i = 0; i < _delayActions.Count; i++)
        {
            List<TriggerAction> list = _delayActions[i];
            bool isRunNext = false;

            while (list.Count != 0)
            {
                if (_actionsThisFrame++ > MaxActionsPerFrame)
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

        for (int i = _delayActions.Count - 1; i >= 0; i--)
        {
            if (_delayActions[i].Count == 0)
            {
                _delayActions.RemoveAt(i);
            }
        }
    }
}
