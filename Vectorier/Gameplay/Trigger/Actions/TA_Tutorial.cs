#nullable disable warnings
using System.Xml;

namespace Vectorier.Gameplay.Trigger.Actions;

public class TA_Tutorial : TriggerAction
{
    private TA_Tutorial(TA_Tutorial copyAction)
        : base(copyAction._ParentLoop)
    {
    }

    public TA_Tutorial(XmlNode node, TriggerLoop parent)
        : base(parent)
    {

    }

    public override void Activate(ref bool isRunNext)
    {
        isRunNext = true;
    }

    public override TriggerAction Copy()
    {
        return new TA_Tutorial(this);
    }

    public override string ToString()
    {
        return "Tutorial";
    }
}
