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
    public class TA_Transformation : TriggerAction
    {
        private Variable _NameVar;

        private Variable _PauseVar;

        private int _Frame;

        private int _CurrentFrame;

        public override int Frames => _ParentLoop.ParentTrigger.ParentElements.Parent.GetTransformationFrame(_NameVar.ValueString);

        private TA_Transformation(TA_Transformation p_copyAction)
            : base(p_copyAction._ParentLoop)
        {
            _NameVar = p_copyAction._NameVar;
            _PauseVar = p_copyAction._PauseVar;
            _CurrentFrame = p_copyAction._CurrentFrame;
            _Frame = p_copyAction._Frame;
        }

        public TA_Transformation(XmlNode p_node, TriggerLoop p_parent, bool p_wait)
            : base(p_parent)
        {
            _CurrentFrame = -1;
            _Frame = 0;
            InitActionVar(p_parent.ParentTrigger, ref _NameVar, p_node.Attributes["Name"].Value);
        }

        public override void Activate(ref bool p_isRunNext)
        {
            if (_CurrentFrame == -1)
            {
                Console.WriteLine("ACTIVATE TRANSFORM NAME: " + _NameVar.ValueString);
                _Frame = _ParentLoop.ParentTrigger.ParentElements.Parent.RunTranformation(_NameVar.ValueString);
                _CurrentFrame = 0;
            }
            if (_CurrentFrame <= _Frame)
            {
                _CurrentFrame++;
                p_isRunNext = false;
            }
            if (_CurrentFrame > _Frame)
            {
                _CurrentFrame = -1;
                p_isRunNext = true;
            }
        }

        public override TriggerAction Copy()
        {
            return new TA_Transformation(this);
        }

        public override void Reset()
        {
            _Frame = 0;
            _CurrentFrame = -1;
        }

        public override string ToString()
        {
            return "Transformation Name=" + _NameVar.DebugStringValue + (_PauseVar == null ? string.Empty : " Stop:1") + " Frames=" + _Frame + " CurrentFrame=" + _CurrentFrame;
        }
    }
}
