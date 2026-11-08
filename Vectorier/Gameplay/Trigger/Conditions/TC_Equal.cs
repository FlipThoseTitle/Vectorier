#nullable disable warnings
using System.Xml;

namespace Vectorier.Gameplay.Trigger.Conditions;

public class TC_Equal : TC_Compare
{
    public TC_Equal(TriggerLoop parent, XmlNode node)
        : base(parent, node)
    {
    }

    public override bool Check()
    {
        if (_IsNot)
        {
            return !_Value2.isEquale(_Value1);
        }
        return _Value2.isEquale(_Value1);
    }

    public override string ToString()
    {
        string text = "Equal: " + (!_IsNot ? "(" : "!(");
        string text2 = text;
        return text2 + _Value1 + "==" + _Value2 + ")";
    }
}
