#nullable disable warnings
using System.Collections.Generic;
using System.Xml;

namespace Vectorier.Gameplay.Trigger.Conditions;

public class TC_OperatorOr : TriggerCondition
{
    public List<TriggerCondition> _Conditions = new List<TriggerCondition>();

    public TC_OperatorOr(TriggerLoop parent, XmlNode node)
        : base(parent, node)
    {
        ParseConditions(node, _Parent, _Conditions);
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
