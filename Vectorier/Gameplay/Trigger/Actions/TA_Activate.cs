#nullable disable warnings
using System.Collections.Generic;
using System.Xml;
using Vectorier.Gameplay.Runners;
using Vectorier.Gameplay.Trigger.Events;

namespace Vectorier.Gameplay.Trigger.Actions;

public class TA_Activate : TriggerAction
{
    private Variable _activationID;

    private TA_Activate(TA_Activate copyAction)
        : base(copyAction._ParentLoop)
    {
        _activationID = copyAction._activationID;
    }

    public TA_Activate(XmlNode node, TriggerLoop parent)
        : base(parent)
    {
        string value = node.Attributes["ActionID"].Value;
        InitActionVar(parent.ParentTrigger, ref _activationID, value);
    }

    public override void Activate(ref bool isRunNext)
    {
        isRunNext = true;
        ActivEvents(_ParentLoop.ParentTrigger.ParentElements.Parent);
    }

    private void ActivEvents(ObjectRunner @object)
    {
        List<TriggerRunner> triggers = @object.Element.Triggers;
        TE_Activate p_event = new TE_Activate(_activationID.ValueString);
        for (int i = 0; i < triggers.Count; i++)
        {
            triggers[i].CheckEvent(p_event, null);
        }

        List<ObjectRunner> childs = @object.Childs;
        for (int j = 0; j < childs.Count; j++)
        {
            ActivEvents(childs[j]);
        }

    }

    public override TriggerAction Copy()
    {
        return new TA_Activate(this);
    }

    public override string ToString()
    {
        return "Activate ID=" + _activationID.DebugStringValue;
    }
}
