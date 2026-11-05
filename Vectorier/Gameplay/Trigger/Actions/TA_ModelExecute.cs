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
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Trigger.Actions
{
    public class TA_ModelExecute : TriggerAction
    {
        private Variable _AnimVar;

        private Variable _FrameVar;

        private TA_ModelExecute(TA_ModelExecute p_copyAction)
            : base(p_copyAction._ParentLoop)
        {
            _AnimVar = p_copyAction._AnimVar;
            _FrameVar = p_copyAction._FrameVar;
        }

        public TA_ModelExecute(XmlNode p_node, TriggerLoop p_parent)
            : base(p_parent)
        {
            InitActionVar(p_parent.ParentTrigger, ref _AnimVar, p_node.Attributes["AnimName"].Value);
            InitActionVar(p_parent.ParentTrigger, ref _FrameVar, p_node.Attributes["AnimFrame"].Value);
        }

        public override void Activate(ref bool p_isRunNext)
        {
            p_isRunNext = true;
            string[] reactions = {
                _AnimVar.ValueString,
                _FrameVar.ValueInt.ToString()
            };
            foreach (var model in _ParentLoop.ParentTrigger.ParentElements.Primitives)
            {
                if (model.Type == ModelType.PrimitiveAnimated)
                {
                    ((PrimitiveAnimatedRunner)model).PlayReaction(reactions);
                }
            }
        }

        public override TriggerAction Copy()
        {
            return new TA_ModelExecute(this);
        }

        public override string ToString()
        {
            return "ModelExecute AnimName" + _AnimVar.DebugStringValue + " AnimFrame" + _FrameVar.DebugStringValue;
        }
    }
}
