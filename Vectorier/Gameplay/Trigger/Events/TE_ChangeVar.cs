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
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Trigger.Events
{
    public class TE_ChangeVar : TriggerEvent
    {
        private bool _IsInit;

        private Variable _Var;

        private int _OldValue;

        /// <param name="p_trigger">
        /// The trigger owning the loop. A watched value such as ?getModel[_$Model].animationName reads the trigger's
        /// variables ($Model, $Node...), so it needs the trigger (without it TF_Model crashed in InitFuncVar).
        /// </param>
        public TE_ChangeVar(XmlNode p_node, TriggerRunner p_trigger)
        {
            _Type = EventType.TET_VAR_CHANGE;
            _Var = Variable.createVariable(p_node.Attributes["Value"]?.Value ?? string.Empty, string.Empty, p_trigger);
        }

        private void Init()
        {
            _IsInit = true;
            _OldValue = _Var.ValueInt;
        }

        public void Reset()
        {
            _IsInit = false;
        }

        public bool IsChange()
        {
            if (!_IsInit)
            {
                Init();
                return false;
            }
            if (_Parent.ParentTrigger.IsActive)
            {
                return _OldValue != _Var.ValueInt;
            }
            return false;
        }
    }
}
