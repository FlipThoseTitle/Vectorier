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

namespace Vectorier.Gameplay.Trigger.Actions
{
    public class TA_Choose : TriggerAction
    {
        private Variable _SetVar;

        private Variable _OrderVar;

        private List<TriggerAction> _Actions = new List<TriggerAction>();

        private List<int> _Indexes = new List<int>();

        private List<int> _SyncIndexes = new List<int>();

        private List<int> _ActivActions = new List<int>();

        private int _Frame;

        private int _CurrentFrame;

        private int _Random;

        private bool _Activated;

        public override int Frames
        {
            get
            {
                int num = 0;
                if (_OrderVar.ValueString == "Sync")
                {
                    int i = 0;
                    for (int count = _Indexes.Count; i < count; i++)
                    {
                        int frames = _Actions[_Indexes[i]].Frames;
                        if (frames > num)
                        {
                            num = frames;
                        }
                    }
                }
                else
                {
                    int j = 0;
                    for (int count2 = _Indexes.Count; j < count2; j++)
                    {
                        num += _Actions[_Indexes[j]].Frames;
                    }
                }
                return num;
            }
        }

        private TA_Choose(TA_Choose p_copy)
            : base(p_copy._ParentLoop)
        {
            _CurrentFrame = p_copy._CurrentFrame;
            _Frame = p_copy._Frame;
            _Indexes = new List<int>(p_copy._Indexes);
            _SyncIndexes = new List<int>(p_copy._SyncIndexes);
            _ActivActions = new List<int>(p_copy._ActivActions);
            _SetVar = p_copy._SetVar;
            _OrderVar = p_copy._OrderVar;
            _Random = p_copy._Random;
            _Activated = p_copy._Activated;
            foreach (TriggerAction action in p_copy._Actions)
            {
                _Actions.Add(action.Copy());
            }
        }

        public TA_Choose(XmlNode p_node, TriggerLoop p_parent)
            : base(p_parent)
        {
            _CurrentFrame = -1;
            _Frame = 0;
            _Random = 0;
            _Activated = true;
            string p_nameOrValue = "0";
            if (p_node.Attributes["Set"] != null)
            {
                p_nameOrValue = p_node.Attributes["Set"].Value;
            }
            string value = p_node.Attributes["Order"].Value;
            InitActionVar(p_parent.ParentTrigger, ref _SetVar, p_nameOrValue);
            InitActionVar(p_parent.ParentTrigger, ref _OrderVar, value);
            ParseActions(p_node, _ParentLoop, _Actions);
            if (_SetVar.ValueInt == 0 || _SetVar.ValueInt > _Actions.Count)
            {
                _SetVar.setValue(_Actions.Count);
            }
            SetExecActions();
        }

        private bool ActivSync()
        {
            int num = 0;
            if (_CurrentFrame == -1)
            {
                _SyncIndexes.Clear();
                num = _SetVar.ValueInt;
                for (int i = 0; i < num; i++)
                {
                    int frames = _Actions[_Indexes[i]].Frames;
                    if (frames > _Frame)
                    {
                        _Frame = frames;
                    }
                    bool isRunNext = false;
                    _Actions[_Indexes[i]].Activate(ref isRunNext);
                    if (!isRunNext)
                    {
                        _SyncIndexes.Add(_Indexes[i]);
                    }
                }
                _CurrentFrame = 1;
                return false;
            }
            List<int> list = new List<int>();
            num = _SyncIndexes.Count;
            for (int j = 0; j < num; j++)
            {
                bool isRunNext2 = false;
                _Actions[_SyncIndexes[j]].Activate(ref isRunNext2);
                if (isRunNext2)
                {
                    list.Add(_SyncIndexes[j]);
                }
            }
            if (list.Count != 0)
            {
                num = list.Count;
                for (int k = 0; k < num; k++)
                {
                    _SyncIndexes.Remove(list[k]);
                }
            }
            if (_CurrentFrame > _Frame)
            {
                _CurrentFrame = -1;
                return true;
            }
            _CurrentFrame++;
            return false;
        }

        private bool ActivStraight()
        {
            int valueInt = _SetVar.ValueInt;
            for (int i = _Frame; i < valueInt; i++)
            {
                bool isRunNext = false;
                _Actions[_Indexes[i]].Activate(ref isRunNext);
                _Frame = i;
                if (!isRunNext)
                {
                    return false;
                }
            }
            _Frame = 0;
            return true;
        }

        private bool ActivRandom()
        {
            int valueInt = _SetVar.ValueInt;
            for (int i = _Frame; i < valueInt; i++)
            {
                if (_Activated)
                {
                    _Random = GetRandActionID();
                }
                _ActivActions.Add(_Random);
                _Activated = false;
                _Actions[_Indexes[_Random]].Activate(ref _Activated);
                _Frame = i;
                if (!_Activated)
                {
                    return false;
                }
            }
            _Frame = 0;
            return true;
        }

        private void SetExecActions()
        {
            _Indexes.Clear();
            if (_SetVar.ValueInt == _Actions.Count || _OrderVar.ValueString == "Random")
            {
                int count = _Actions.Count;
                for (int i = 0; i < count; i++)
                {
                    _Indexes.Add(i);
                }
            }
            else if (_OrderVar.ValueString == "Sync")
            {
                int valueInt = _SetVar.ValueInt;
                for (int j = 0; j < valueInt; j++)
                {
                    int item;
                    do
                    {
                        item = Random.Range(0, _Actions.Count);
                    }
                    while (_Indexes.Contains(item));
                    _Indexes.Add(item);
                }
            }
            else
            {
                if (!(_OrderVar.ValueString == "Straight"))
                {
                    return;
                }
                int valueInt2 = _SetVar.ValueInt;
                for (int k = 0; k < valueInt2; k++)
                {
                    int item2;
                    do
                    {
                        item2 = Random.Range(0, _Actions.Count);
                    }
                    while (_Indexes.Contains(item2));
                    _Indexes.Add(item2);
                }
                _Indexes.Sort();
                valueInt2 = _Indexes.Count;
            }
        }

        private bool IsActionActivate(int p_value)
        {
            return _ActivActions.Contains(p_value);
        }

        private int GetRandActionID()
        {
            int num = 0;
            do
            {
                num = Random.Range(0, _Actions.Count);
            }
            while (IsActionActivate(num));
            return num;
        }

        public override void Activate(ref bool p_isRunNext)
        {
            switch (_OrderVar.ValueString)
            {
                case "Sync":
                    p_isRunNext = ActivSync();
                    break;
                case "Straight":
                    p_isRunNext = ActivStraight();
                    break;
                case "Random":
                    p_isRunNext = ActivRandom();
                    break;
            }
        }

        public override TriggerAction Copy()
        {
            return new TA_Choose(this);
        }

        public override void Reset()
        {
            _CurrentFrame = -1;
            _SyncIndexes.Clear();
            _Activated = true;
            _ActivActions.Clear();
            _Frame = 0;
            foreach (var action in _Actions)
            {
                action.Reset();
            }
            SetExecActions();
        }

        public override string ToString()
        {
            string text = "Choose: Order:" + _OrderVar.DebugStringValue;
            string text2 = text;
            text = text2 + " Set:" + _SetVar.DebugStringValue + " Frames=" + _Frame + " CurFrame=" + _CurrentFrame;
            foreach (TriggerAction action in _Actions)
            {
                text = text + "\n     " + action;
            }
            return text;
        }
    }
}
