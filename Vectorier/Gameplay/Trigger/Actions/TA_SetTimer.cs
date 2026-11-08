#nullable disable warnings
using System.Xml;

namespace Vectorier.Gameplay.Trigger.Actions;

public class TA_SetTimer : TriggerAction
{
    private Variable _framesVar;

    private TA_SetTimer(TA_SetTimer copyAction)
        : base(copyAction._ParentLoop)
    {
        _framesVar = copyAction._framesVar;
    }

    public TA_SetTimer(XmlNode node, TriggerLoop parent)
        : base(parent)
    {
        InitActionVar(parent.ParentTrigger, ref _framesVar, node.Attributes["Frames"].Value);
    }

    public override void Activate(ref bool isRunNext)
    {
        isRunNext = true;
        _ParentLoop.ParentTrigger.SetTimer(_framesVar.ValueInt);
    }

    public override TriggerAction Copy()
    {
        return new TA_SetTimer(this);
    }

    public override string ToString()
    {
        return "SetTimer Frames=" + _framesVar.DebugStringValue;
    }
}
