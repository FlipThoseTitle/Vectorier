#nullable disable warnings
using System.Xml;

namespace Vectorier.Gameplay.Trigger.Actions;

public class TA_EndGame : TriggerAction
{
    private Variable _timeVar;

    private Variable _resultVar;

    private Variable _modelVar;

    private TA_EndGame(TA_EndGame copyAction)
        : base(copyAction._ParentLoop)
    {
        _resultVar = copyAction._resultVar;
        _timeVar = copyAction._timeVar;
        _modelVar = copyAction._modelVar;
    }

    public TA_EndGame(XmlNode node, TriggerLoop parent)
        : base(parent)
    {
        string value = node.Attributes["Result"].Value;
        string p_nameOrValue = "80";
        if (node.Attributes["Frames"] != null)
        {
            p_nameOrValue = node.Attributes["Frames"].Value;
        }
        string value2 = node.Attributes["Model"].Value;
        InitActionVar(parent.ParentTrigger, ref _resultVar, value);
        InitActionVar(parent.ParentTrigger, ref _timeVar, p_nameOrValue);
        InitActionVar(parent.ParentTrigger, ref _modelVar, value2);
    }

    public override void Activate(ref bool isRunNext)
    {
        isRunNext = true;
        string valueString = _modelVar.ValueString;
        switch (_resultVar.ValueString)
        {
            case "Win":
                LevelMainController.current.Win(GetModel(valueString), _timeVar.ValueInt / 60f);
                break;
            case "Loss":
                LevelMainController.current.Loss(GetModel(valueString), _timeVar.ValueInt / 60f);
                break;
            case "Death":
                LevelMainController.current.Death(GetModel(valueString), _timeVar.ValueInt / 60f);
                break;
        }
    }

    public override TriggerAction Copy()
    {
        return new TA_EndGame(this);
    }

    public override string ToString()
    {
        return "EndGame Model=" + _modelVar.DebugStringValue + " Result=" + _resultVar.DebugStringValue + " Frames" + _timeVar.DebugStringValue;
    }
}
