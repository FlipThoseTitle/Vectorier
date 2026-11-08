#nullable disable warnings
using System.Collections.Generic;
using System.Xml;

namespace Vectorier.Gameplay.Trigger.Conditions;

public class TC_OperatorAnd : TriggerCondition
{
    public List<TriggerCondition> _Conditions = new List<TriggerCondition>();

    public TC_OperatorAnd(TriggerLoop parent, XmlNode node)
        : base(parent, node)
    {
        ParseConditions(node, _Parent, _Conditions);
    }

    public override bool Check()
    {
        for (int i = 0; i < _Conditions.Count; i++)
        {
            if (!_Conditions[i].Check())
            {
                return _IsNot;
            }
        }
        return !_IsNot;
    }

    public override string ToString()
    {
        string text = "And: ";
        foreach (TriggerCondition condition in _Conditions)
        {
            text = text + "\n    " + condition;
        }
        return text;
    }
}
