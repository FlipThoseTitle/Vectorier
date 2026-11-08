#nullable disable warnings
using System.Xml;

namespace Vectorier.Gameplay.Trigger.Conditions;

public class TC_GreaterEqual : TC_Compare
{
    public TC_GreaterEqual(TriggerLoop parent, XmlNode node)
        : base(parent, node)
    {
    }

    public override bool Check()
    {
        if (_IsNot)
        {
            return !_Value1.isGreaterEqual(_Value2);
        }
        return _Value1.isGreaterEqual(_Value2);
    }

    public override string ToString()
    {
        string text = "GreaterEqual: " + (!_IsNot ? "(" : "!(");
        string text2 = text;
        return text2 + _Value1 + "<=" + _Value2 + ")";
    }
}
