#nullable disable warnings
using System.Xml;
using Vectorier.Gameplay.Models;

namespace Vectorier.Gameplay.Trigger.Actions;

public class TA_Kill : TriggerAction
{
    private Variable _modelVar;

    public TA_Kill(TA_Kill copyAction)
        : base(copyAction._ParentLoop)
    {
        _modelVar = copyAction._modelVar;
    }

    public TA_Kill(XmlNode node, TriggerLoop parent)
        : base(parent)
    {
        InitActionVar(parent.ParentTrigger, ref _modelVar, node.Attributes["Model"].Value);
    }

    public override void Activate(ref bool isRunNext)
    {
        isRunNext = true;
        ModelHuman model = GetModel(_modelVar.ValueString);
        if (model != null)
        {
            model.StartPhysics();
        }
    }

    public override TriggerAction Copy()
    {
        return new TA_Kill(this);
    }

    public override string ToString()
    {
        return "Kill Model=" + _modelVar.DebugStringValue;
    }
}
