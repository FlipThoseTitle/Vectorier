#nullable disable warnings
using System.Xml;

namespace Vectorier.Gameplay.Trigger.Actions;

public class TA_Spawn : TriggerAction
{
    private Variable _spawnVar;

    private Variable _modelVar;

    private TA_Spawn(TA_Spawn copyAction)
        : base(copyAction._ParentLoop)
    {
        _spawnVar = copyAction._spawnVar;
        _modelVar = copyAction._modelVar;
    }

    public TA_Spawn(XmlNode node, TriggerLoop parent)
        : base(parent)
    {
        InitActionVar(parent.ParentTrigger, ref _modelVar, node.Attributes["Model"].Value);
        if (node.Attributes["Spawn"] != null)
        {
            InitActionVar(parent.ParentTrigger, ref _spawnVar, node.Attributes["Spawn"].Value);
        }
    }

    public override void Activate(ref bool isRunNext)
    {
        isRunNext = true;
        var model = GetModel(_modelVar.ValueString);
        model.Reset();
        model.IsVisible = true;
        model.IsDelayEnd = true;
        model.Play(_ParentLoop.ParentTrigger.GetSpawnByName(_spawnVar.ValueString));
    }

    public override TriggerAction Copy()
    {
        return new TA_Spawn(this);
    }

    public override string ToString()
    {
        return "Spawn Model=" + _modelVar.DebugStringValue + " Spawn" + _spawnVar == null ? "null" : _spawnVar.DebugStringValue;
    }
}
