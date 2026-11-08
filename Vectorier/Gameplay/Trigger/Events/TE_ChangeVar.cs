#nullable disable warnings
using System.Xml;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Trigger.Events;

public class TE_ChangeVar : TriggerEvent
{
    private bool _isInit;

    private Variable _var;

    private int _oldValue;

    /// <param name="trigger">
    /// The trigger owning the loop. A watched value such as ?getModel[_$Model].animationName reads the trigger's
    /// variables ($Model, $Node...), so it needs the trigger (without it TF_Model crashed in InitFuncVar).
    /// </param>
    public TE_ChangeVar(XmlNode node, TriggerRunner trigger)
    {
        _Type = EventType.TET_VAR_CHANGE;
        _var = Variable.createVariable(node.Attributes["Value"]?.Value ?? string.Empty, string.Empty, trigger);
    }

    private void Init()
    {
        _isInit = true;
        _oldValue = _var.ValueInt;
    }

    public void Reset()
    {
        _isInit = false;
    }

    public bool IsChange()
    {
        if (!_isInit)
        {
            Init();
            return false;
        }
        if (_Parent.ParentTrigger.IsActive)
        {
            return _oldValue != _var.ValueInt;
        }
        return false;
    }
}
