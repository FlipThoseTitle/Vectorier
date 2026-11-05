// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Vectorier.Gameplay.Trigger.Actions
{
    public class TA_AppendValue : TriggerAction
    {
        private Variable _SetVar;

        private Variable _ValueVar;

        private TA_AppendValue(TA_AppendValue p_copyAction)
            : base(p_copyAction._ParentLoop)
        {
            _SetVar = p_copyAction._SetVar;
            _ValueVar = p_copyAction._ValueVar;
        }

        public TA_AppendValue(XmlNode p_node, TriggerLoop p_parent)
            : base(p_parent)
        {
            _ValueVar = null;
            string value = p_node.Attributes["Name"].Value;
            string value2 = p_node.Attributes["Value"].Value;
            if (value2[0] == '_')
            {
                _ValueVar = _ParentLoop.GetParentVar(value2);
                if (_ValueVar == null)
                {
                    Console.WriteLine("Error create Action SetVariable not found" + value2, true);
                }
            }
            else
            {
                _ValueVar = Variable.createVariable(value2, string.Empty, p_parent.ParentTrigger);
            }
            if (value[0] == '?')
            {
                _SetVar = Variable.createVariable(value, string.Empty, p_parent.ParentTrigger);
            }
            else
            {
                _SetVar = _ParentLoop.GetParentVar("_" + value);
            }
            if (_SetVar == null)
            {
                Console.WriteLine("Error create Action SetVariable not found = " + value);
            }
        }

        public override void Activate(ref bool p_isRunNext)
        {
            p_isRunNext = true;
            switch (_ValueVar.Type)
            {
                case VariableTypeE.VT_DOUBLE:
                    _SetVar.appendValue(_ValueVar.ValueFloat);
                    break;
                case VariableTypeE.VT_STRING:
                    _SetVar.appendValue(_ValueVar.ValueString);
                    break;
                default:
                    _SetVar.appendValue(_ValueVar.ValueInt);
                    break;
            }
        }

        public override TriggerAction Copy()
        {
            return new TA_AppendValue(this);
        }

        public override string ToString()
        {
            return "AppendValue Var:" + _SetVar + " Value:" + _ValueVar.DebugStringValue;
        }
    }
}
