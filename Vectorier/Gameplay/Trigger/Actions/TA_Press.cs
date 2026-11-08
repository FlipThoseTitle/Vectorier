#nullable disable warnings
using System.Xml;
using Vectorier.Gameplay.Controllers;
using Vectorier.Gameplay.Models;

namespace Vectorier.Gameplay.Trigger.Actions;

public class TA_Press : TriggerAction
{
    private Variable _modelNameVar;

    private Variable _keyVar;

    private TA_Press(TA_Press copyAction)
        : base(copyAction._ParentLoop)
    {
        _keyVar = copyAction._keyVar;
        _modelNameVar = copyAction._modelNameVar;
    }

    public TA_Press(XmlNode node, TriggerLoop parent)
        : base(parent)
    {
        InitActionVar(parent.ParentTrigger, ref _keyVar, node.Attributes["Key"].Value);
        InitActionVar(parent.ParentTrigger, ref _modelNameVar, node.Attributes["Model"].Value);
    }

    public override void Activate(ref bool isRunNext)
    {
        isRunNext = true;
        string valueString = _modelNameVar.ValueString;
        ModelHuman model = GetModel(valueString);
        if (model != null)
        {
            model.ControllerKeys.SetKeyVariable_force(new KeyVariables(_keyVar.ValueString));
        }
    }

    public override TriggerAction Copy()
    {
        return new TA_Press(this);
    }

    public override string ToString()
    {
        return "Press Model=" + _modelNameVar.DebugStringValue + " Key=" + _keyVar.DebugStringValue;
    }
}
