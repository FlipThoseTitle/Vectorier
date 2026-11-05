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
    public class TA_SetTimer : TriggerAction
    {
        private Variable _FramesVar;

        private TA_SetTimer(TA_SetTimer p_copyAction)
            : base(p_copyAction._ParentLoop)
        {
            _FramesVar = p_copyAction._FramesVar;
        }

        public TA_SetTimer(XmlNode p_node, TriggerLoop p_parent)
            : base(p_parent)
        {
            InitActionVar(p_parent.ParentTrigger, ref _FramesVar, p_node.Attributes["Frames"].Value);
        }

        public override void Activate(ref bool p_isRunNext)
        {
            p_isRunNext = true;
            _ParentLoop.ParentTrigger.SetTimer(_FramesVar.ValueInt);
        }

        public override TriggerAction Copy()
        {
            return new TA_SetTimer(this);
        }

        public override string ToString()
        {
            return "SetTimer Frames=" + _FramesVar.DebugStringValue;
        }
    }
}
