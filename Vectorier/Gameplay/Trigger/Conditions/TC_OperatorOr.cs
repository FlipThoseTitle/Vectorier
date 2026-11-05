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

namespace Vectorier.Gameplay.Trigger.Conditions
{
    public class TC_OperatorOr : TriggerCondition
    {
        public List<TriggerCondition> _Conditions = new List<TriggerCondition>();

        public TC_OperatorOr(TriggerLoop p_parent, XmlNode p_node)
            : base(p_parent, p_node)
        {
            ParseConditions(p_node, _Parent, _Conditions);
        }

        public override bool Check()
        {
            foreach (TriggerCondition condition in _Conditions)
            {
                bool flag = condition.Check();
                if (flag)
                {
                    return !_IsNot;
                }
            }
            return _IsNot;
        }

        public override string ToString()
        {
            string text = "Or: ";
            foreach (TriggerCondition condition in _Conditions)
            {
                text = text + "\n    " + condition;
            }
            return text;
        }
    }
}
