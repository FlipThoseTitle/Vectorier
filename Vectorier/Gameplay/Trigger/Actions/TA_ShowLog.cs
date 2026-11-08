#nullable disable warnings
using System.Xml;

namespace Vectorier.Gameplay.Trigger.Actions;

public class TA_ShowLog : TriggerAction
{
    private TA_ShowLog(TA_ShowLog copyAction)
        : base(copyAction._ParentLoop)
    {
    }

    public TA_ShowLog(XmlNode node, TriggerLoop parent)
        : base(parent)
    {
    }

    public override void Activate(ref bool isRunNext)
    {
        isRunNext = true;
    }

    public override TriggerAction Copy()
    {
        return new TA_ShowLog(this);
    }

    public override string ToString()
    {
        return "ShowLog";
    }
}
