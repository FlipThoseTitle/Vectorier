#nullable disable warnings
using System.Xml;
using Vectorier.Gameplay.Models;

namespace Vectorier.Gameplay.Trigger.Actions;

public class TA_Control : TriggerAction
{
    private Variable _modelNameVar;

    private Variable _switchVar;

    private TA_Control(TA_Control copyAction)
        : base(copyAction._ParentLoop)
    {
        _modelNameVar = copyAction._modelNameVar;
        _switchVar = copyAction._switchVar;
    }

    public TA_Control(XmlNode node, TriggerLoop parent)
        : base(parent)
    {
        string value = node.Attributes["Model"].Value;
        string value2 = node.Attributes["Switch"].Value;
        InitActionVar(parent.ParentTrigger, ref _switchVar, value2);
        InitActionVar(parent.ParentTrigger, ref _modelNameVar, value);
    }

    public override void Activate(ref bool isRunNext)
    {
        isRunNext = true;
        string valueString = _modelNameVar.ValueString;
        ModelHuman model = GetModel(valueString);
        if (model != null)
        {
            model.ControllerKeys.Enable = _switchVar.ValueString == "On";
            model.ControllerKeys.ClearAnimation();
        }
    }

    public override TriggerAction Copy()
    {
        return new TA_Control(this);
    }

    public override string ToString()
    {
        return "Control: Model=" + _modelNameVar.DebugStringValue + " Switch:" + _switchVar.DebugStringValue;
    }
}
