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
    public class TA_Wait : TriggerAction
    {
        private Variable _FrameVar;

        private int _CurrentFrame;

        public override int Frames => _FrameVar.ValueInt;

        private TA_Wait(TA_Wait p_copyAction)
            : base(p_copyAction._ParentLoop)
        {
            _CurrentFrame = p_copyAction._CurrentFrame;
            _FrameVar = p_copyAction._FrameVar;
        }

        public TA_Wait(XmlNode p_node, TriggerLoop p_parent)
            : base(p_parent)
        {
            InitActionVar(p_parent.ParentTrigger, ref _FrameVar, p_node.Attributes["Frames"].Value);
        }

        public override void Activate(ref bool p_isRunNext)
        {
            if (_CurrentFrame > _FrameVar.ValueInt)
            {
                p_isRunNext = true;
                _CurrentFrame = 0;
            }
            else
            {
                _CurrentFrame++;
                p_isRunNext = false;
            }
        }

        public override TriggerAction Copy()
        {
            return new TA_Wait(this);
        }

        public override void Reset()
        {
            _CurrentFrame = 0;
        }

        public override string ToString()
        {
            return "Wait Frames=" + _FrameVar.DebugStringValue + " CurFrame=" + _CurrentFrame;
        }
    }
}
