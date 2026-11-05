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
using Vectorier.Gameplay.Trigger.Events;

namespace Vectorier.Gameplay.Trigger.Actions
{
    public class TA_Activate : TriggerAction
    {
        private Variable _ActivationID;

        private TA_Activate(TA_Activate p_copyAction)
            : base(p_copyAction._ParentLoop)
        {
            _ActivationID = p_copyAction._ActivationID;
        }

        public TA_Activate(XmlNode p_node, TriggerLoop p_parent)
            : base(p_parent)
        {
            string value = p_node.Attributes["ActionID"].Value;
            InitActionVar(p_parent.ParentTrigger, ref _ActivationID, value);
        }

        public override void Activate(ref bool p_isRunNext)
        {
            p_isRunNext = true;
            ActivEvents(_ParentLoop.ParentTrigger.ParentElements.Parent);
        }

        private void ActivEvents(ObjectRunner p_object)
        {
            List<TriggerRunner> triggers = p_object.Element.Triggers;
            TE_Activate p_event = new TE_Activate(_ActivationID.ValueString);
            for (int i = 0; i < triggers.Count; i++)
            {
                triggers[i].CheckEvent(p_event, null);
            }

            List<ObjectRunner> childs = p_object.Childs;
            for (int j = 0; j < childs.Count; j++)
            {
                ActivEvents(childs[j]);
            }

        }

        public override TriggerAction Copy()
        {
            return new TA_Activate(this);
        }

        public override string ToString()
        {
            return "Activate ID=" + _ActivationID.DebugStringValue;
        }
    }
}
