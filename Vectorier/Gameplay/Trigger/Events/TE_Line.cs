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

namespace Vectorier.Gameplay.Trigger.Events
{
    public class TE_Line : TriggerEvent
    {
        private Variable _Line;

        private static int _CounterID;

        /// <summary>The editor's addition: a new game counts its lines from the start.</summary>
        public static void ResetCounter() => _CounterID = 0;

        private string _TypeStr;

        public int ID
        {
            get;
            set;
        }

        public TE_Line(TriggerLoop p_parent, XmlNode p_node, int p_ID = -1)
        {
            _Type = EventType.TET_LINE;
            if (p_parent == null)
            {
                ID = p_ID;
                return;
            }
            ID = _CounterID++;
            string value = p_node.Attributes["Position"].Value;
            if (value[0] == '_')
            {
                _Line = p_parent.ParentTrigger.GetVar(value);
            }
            else
            {
                _Line = Variable.createVariable(value, string.Empty);
            }
            _TypeStr = p_node.Attributes["Type"].Value;
            p_parent.SetLine(_TypeStr, _Line, ID);
        }

        public override bool IsEqual(TriggerEvent p_value)
        {
            if (!base.IsEqual(p_value))
            {
                return false;
            }
            return ID == (p_value as TE_Line).ID;
        }
    }
}
