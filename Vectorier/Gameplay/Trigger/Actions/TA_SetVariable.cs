#nullable disable warnings
using System;
using System.Xml;

namespace Vectorier.Gameplay.Trigger.Actions;

public class TA_SetVariable : TriggerAction
{
    private Variable _setVar;

    private Variable _valueVar;

    private TA_SetVariable(TA_SetVariable copyAction)
        : base(copyAction._ParentLoop)
    {
        _setVar = copyAction._setVar;
        _valueVar = copyAction._valueVar;
    }

    public TA_SetVariable(XmlNode node, TriggerLoop parent)
        : base(parent)
    {
        string value = node.Attributes["Name"].Value;
        string value2 = node.Attributes["Value"].Value;
        if (value2[0] == '_')
        {
            _valueVar = _ParentLoop.GetParentVar(value2);
            if (_valueVar == null)
            {
                Console.WriteLine("Error create Action SetVariable no found : " + value2);
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
            Console.WriteLine("Error create Action SetVariable not found =" + value);
        }
    }

    public override void Activate(ref bool isRunNext)
    {
        isRunNext = true;
        switch (_valueVar.Type)
        {
            case VariableTypeE.VT_DOUBLE:
            case VariableTypeE.VT_FUNCTION:
            case VariableTypeE.VT_EXPRESSION:
                _setVar.setValue(_valueVar.ValueFloat);
                break;
            case VariableTypeE.VT_STRING:
                _setVar.setValue(_valueVar.ValueString);
                break;
            default:
                _setVar.setValue(_valueVar.ValueInt);
                break;
        }
    }

    public override TriggerAction Copy()
    {
        return new TA_SetVariable(this);
    }

    public override string ToString()
    {
        return "SetVariable Var:" + _setVar + " Value=" + _valueVar.DebugStringValue;
    }
}
