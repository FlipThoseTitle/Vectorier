#nullable disable warnings
using System.Xml;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Trigger.Actions;

public class TA_ModelExecute : TriggerAction
{
    private Variable _animVar;

    private Variable _frameVar;

    private TA_ModelExecute(TA_ModelExecute copyAction)
        : base(copyAction._ParentLoop)
    {
        _animVar = copyAction._animVar;
        _frameVar = copyAction._frameVar;
    }

    public TA_ModelExecute(XmlNode node, TriggerLoop parent)
        : base(parent)
    {
        InitActionVar(parent.ParentTrigger, ref _animVar, node.Attributes["AnimName"].Value);
        InitActionVar(parent.ParentTrigger, ref _frameVar, node.Attributes["AnimFrame"].Value);
    }

    public override void Activate(ref bool isRunNext)
    {
        isRunNext = true;
        string[] reactions = {
            _animVar.ValueString,
            _frameVar.ValueInt.ToString()
        };
        foreach (var model in _ParentLoop.ParentTrigger.ParentElements.Primitives)
        {
            if (model.Type == ModelType.PrimitiveAnimated)
            {
                ((PrimitiveAnimatedRunner)model).PlayReaction(reactions);
            }
        }
    }

    public override TriggerAction Copy()
    {
        return new TA_ModelExecute(this);
    }

    public override string ToString()
    {
        return "ModelExecute AnimName" + _animVar.DebugStringValue + " AnimFrame" + _frameVar.DebugStringValue;
    }
}
