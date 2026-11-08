#nullable disable warnings
using System.Xml;

namespace Vectorier.Gameplay.Trigger.Conditions;

public class TC_Compare : TriggerCondition
{
    protected Variable _Value1;

    protected Variable _Value2;

    public TC_Compare(TriggerLoop parent, XmlNode node)
        : base(parent, node)
    {
        _Type = ConditionsType.TCT_None;
        Parse(node);
    }

    protected void Parse(XmlNode node)
    {
        _Value1 = GetOrCreateVar(node.Attributes["Value1"] == null ? node.Attributes["Value"].Value : node.Attributes["Value1"].Value);
        _Value2 = GetOrCreateVar(node.Attributes["Value2"] == null ? node.Attributes["Than"].Value : node.Attributes["Value2"].Value);
    }

    public override bool Check()
    {
        return false;
    }
}
