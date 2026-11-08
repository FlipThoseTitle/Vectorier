#nullable disable warnings
using System;
using System.Xml;

namespace Vectorier.Gameplay.Trigger.Actions;

public class TA_AppendValue : TriggerAction
{
    private Variable _setVar;

    private Variable _valueVar;

    private TA_AppendValue(TA_AppendValue copyAction)
        : base(copyAction._ParentLoop)
    {
        _setVar = copyAction._setVar;
        _valueVar = copyAction._valueVar;
    }

    public TA_AppendValue(XmlNode node, TriggerLoop parent)
        : base(parent)
    {
        _valueVar = null;
        string value = node.Attributes["Name"].Value;
        string value2 = node.Attributes["Value"].Value;
        if (value2[0] == '_')
        {
            _valueVar = _ParentLoop.GetParentVar(value2);
            if (_valueVar == null)
            {
                Console.WriteLine("Error create Action SetVariable not found" + value2, true);
            }
        }
        else
        {
            _valueVar = Variable.createVariable(value2, string.Empty, parent.ParentTrigger);
        }
        if (value[0] == '?')
        {
            _setVar = Variable.createVariable(value, string.Empty, parent.ParentTrigger);
        }
        else
        {
            _setVar = _ParentLoop.GetParentVar("_" + value);
        }
        if (_setVar == null)
        {
            Console.WriteLine("Error create Action SetVariable not found = " + value);
        }
    }

    public override void Activate(ref bool isRunNext)
    {
        isRunNext = true;
        switch (_valueVar.Type)
        {
            case VariableTypeE.VT_DOUBLE:
                _setVar.appendValue(_valueVar.ValueFloat);
                break;
            case VariableTypeE.VT_STRING:
                _setVar.appendValue(_valueVar.ValueString);
                break;
            default:
                _setVar.appendValue(_valueVar.ValueInt);
                break;
        }
    }

    public override TriggerAction Copy()
    {
        return new TA_AppendValue(this);
    }

    public override string ToString()
    {
        return "AppendValue Var:" + _setVar + " Value:" + _valueVar.DebugStringValue;
    }
}
