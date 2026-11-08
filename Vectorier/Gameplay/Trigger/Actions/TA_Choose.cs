#nullable disable warnings
using System.Collections.Generic;
using System.Xml;

namespace Vectorier.Gameplay.Trigger.Actions;

public class TA_Choose : TriggerAction
{
    private Variable _setVar;

    private Variable _orderVar;

    private List<TriggerAction> _actions = new List<TriggerAction>();

    private List<int> _indexes = new List<int>();

    private List<int> _syncIndexes = new List<int>();

    private List<int> _activActions = new List<int>();

    private int _frame;

    private int _currentFrame;

    private int _random;

    private bool _activated;

    public override int Frames
    {
        get
        {
            int num = 0;
            if (_orderVar.ValueString == "Sync")
            {
                int i = 0;
                for (int count = _indexes.Count; i < count; i++)
                {
                    int frames = _actions[_indexes[i]].Frames;
                    if (frames > num)
                    {
                        num = frames;
                    }
                }
            }
            else
            {
                int j = 0;
                for (int count2 = _indexes.Count; j < count2; j++)
                {
                    num += _actions[_indexes[j]].Frames;
                }
            }
            return num;
        }
    }

    private TA_Choose(TA_Choose copy)
        : base(copy._ParentLoop)
    {
        _currentFrame = copy._currentFrame;
        _frame = copy._frame;
        _indexes = new List<int>(copy._indexes);
        _syncIndexes = new List<int>(copy._syncIndexes);
        _activActions = new List<int>(copy._activActions);
        _setVar = copy._setVar;
        _orderVar = copy._orderVar;
        _random = copy._random;
        _activated = copy._activated;
        foreach (TriggerAction action in copy._actions)
        {
            _actions.Add(action.Copy());
        }
    }

    public TA_Choose(XmlNode node, TriggerLoop parent)
        : base(parent)
    {
        _currentFrame = -1;
        _frame = 0;
        _random = 0;
        _activated = true;
        string p_nameOrValue = "0";
        if (node.Attributes["Set"] != null)
        {
            p_nameOrValue = node.Attributes["Set"].Value;
        }
        string value = node.Attributes["Order"].Value;
        InitActionVar(parent.ParentTrigger, ref _setVar, p_nameOrValue);
        InitActionVar(parent.ParentTrigger, ref _orderVar, value);
        ParseActions(node, _ParentLoop, _actions);
        if (_setVar.ValueInt == 0 || _setVar.ValueInt > _actions.Count)
        {
            _setVar.setValue(_actions.Count);
        }
        SetExecActions();
    }

    private bool ActivSync()
    {
        int num = 0;
        if (_currentFrame == -1)
        {
            _syncIndexes.Clear();
            num = _setVar.ValueInt;
            for (int i = 0; i < num; i++)
            {
                int frames = _actions[_indexes[i]].Frames;
                if (frames > _frame)
                {
                    _frame = frames;
                }
                bool isRunNext = false;
                _actions[_indexes[i]].Activate(ref isRunNext);
                if (!isRunNext)
                {
                    _syncIndexes.Add(_indexes[i]);
                }
            }
            _currentFrame = 1;
            return false;
        }
        List<int> list = new List<int>();
        num = _syncIndexes.Count;
        for (int j = 0; j < num; j++)
        {
            bool isRunNext2 = false;
            _actions[_syncIndexes[j]].Activate(ref isRunNext2);
            if (isRunNext2)
            {
                list.Add(_syncIndexes[j]);
            }
        }
        if (list.Count != 0)
        {
            num = list.Count;
            for (int k = 0; k < num; k++)
            {
                _syncIndexes.Remove(list[k]);
            }
        }
        if (_currentFrame > _frame)
        {
            _currentFrame = -1;
            return true;
        }
        _currentFrame++;
        return false;
    }

    private bool ActivStraight()
    {
        int valueInt = _setVar.ValueInt;
        for (int i = _frame; i < valueInt; i++)
        {
            bool isRunNext = false;
            _actions[_indexes[i]].Activate(ref isRunNext);
            _frame = i;
            if (!isRunNext)
            {
                return false;
            }
        }
        _frame = 0;
        return true;
    }

    private bool ActivRandom()
    {
        int valueInt = _setVar.ValueInt;
        for (int i = _frame; i < valueInt; i++)
        {
            if (_activated)
            {
                _random = GetRandActionID();
            }
            _activActions.Add(_random);
            _activated = false;
            _actions[_indexes[_random]].Activate(ref _activated);
            _frame = i;
            if (!_activated)
            {
                return false;
            }
        }
        _frame = 0;
        return true;
    }

    private void SetExecActions()
    {
        _indexes.Clear();
        if (_setVar.ValueInt == _actions.Count || _orderVar.ValueString == "Random")
        {
            int count = _actions.Count;
            for (int i = 0; i < count; i++)
            {
                _indexes.Add(i);
            }
        }
        else if (_orderVar.ValueString == "Sync")
        {
            int valueInt = _setVar.ValueInt;
            for (int j = 0; j < valueInt; j++)
            {
                int item;
                do
                {
                    item = Random.Range(0, _actions.Count);
                }
                while (_indexes.Contains(item));
                _indexes.Add(item);
            }
        }
        else
        {
            if (!(_orderVar.ValueString == "Straight"))
            {
                return;
            }
            int valueInt2 = _setVar.ValueInt;
            for (int k = 0; k < valueInt2; k++)
            {
                int item2;
                do
                {
                    item2 = Random.Range(0, _actions.Count);
                }
                while (_indexes.Contains(item2));
                _indexes.Add(item2);
            }
            _indexes.Sort();
            valueInt2 = _indexes.Count;
        }
    }

    private bool IsActionActivate(int value)
    {
        return _activActions.Contains(value);
    }

    private int GetRandActionID()
    {
        int num = 0;
        do
        {
            num = Random.Range(0, _actions.Count);
        }
        while (IsActionActivate(num));
        return num;
    }

    public override void Activate(ref bool isRunNext)
    {
        switch (_orderVar.ValueString)
        {
            case "Sync":
                isRunNext = ActivSync();
                break;
            case "Straight":
                isRunNext = ActivStraight();
                break;
            case "Random":
                isRunNext = ActivRandom();
                break;
        }
    }

    public override TriggerAction Copy()
    {
        return new TA_Choose(this);
    }

    public override void Reset()
    {
        _currentFrame = -1;
        _syncIndexes.Clear();
        _activated = true;
        _activActions.Clear();
        _frame = 0;
        foreach (var action in _actions)
        {
            action.Reset();
        }
        SetExecActions();
    }

    public override string ToString()
    {
        string text = "Choose: Order:" + _orderVar.DebugStringValue;
        string text2 = text;
        text = text2 + " Set:" + _setVar.DebugStringValue + " Frames=" + _frame + " CurFrame=" + _currentFrame;
        foreach (TriggerAction action in _actions)
        {
            text = text + "\n     " + action;
        }
        return text;
    }
}
