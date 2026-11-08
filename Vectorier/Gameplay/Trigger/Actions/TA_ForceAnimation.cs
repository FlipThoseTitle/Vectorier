#nullable disable warnings
using System.Xml;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Trigger.Actions;

public class TA_ForceAnimation : TriggerAction
{
    private Variable _framesVar;

    private Variable _nameVar;

    private Variable _modelNameVar;

    private Variable _directionVar;

    private TA_ForceAnimation(TA_ForceAnimation copyAction)
        : base(copyAction._ParentLoop)
    {
        _framesVar = copyAction._framesVar;
        _nameVar = copyAction._nameVar;
        _modelNameVar = copyAction._modelNameVar;
        _directionVar = copyAction._directionVar;
    }

    public TA_ForceAnimation(XmlNode node, TriggerLoop parent)
        : base(parent)
    {
        InitActionVar(parent.ParentTrigger, ref _framesVar, node.Attributes["Frame"].ParseString("-1"));
        InitActionVar(parent.ParentTrigger, ref _nameVar, node.Attributes["Name"].ParseString());
        InitActionVar(parent.ParentTrigger, ref _modelNameVar, node.Attributes["Model"].ParseString());
        InitActionVar(parent.ParentTrigger, ref _directionVar, node.Attributes["Reversed"].ParseString("0"));
    }

    public override void Activate(ref bool isRunNext)
    {
        isRunNext = true;
        string valueString = _modelNameVar.ValueString;
        ModelHuman model = GetModel(valueString);
        if (model != null)
        {
            model.PlayAnimation(_nameVar.ValueString, _directionVar.ValueInt == 1, _framesVar.ValueInt);
        }
    }

    public override TriggerAction Copy()
    {
        return new TA_ForceAnimation(this);
    }

    public override string ToString()
    {
        return "ForceAnimation Model=" + _modelNameVar.DebugStringValue + " Name=" + _nameVar.DebugStringValue + " Frame=" + _framesVar.DebugStringValue + " Revers=" + _directionVar.DebugStringValue;
    }
}
