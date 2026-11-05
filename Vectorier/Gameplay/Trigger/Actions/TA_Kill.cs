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

namespace Vectorier.Gameplay.Trigger.Actions
{
    public class TA_Kill : TriggerAction
    {
        private Variable _ModelVar;

        public TA_Kill(TA_Kill p_copyAction)
            : base(p_copyAction._ParentLoop)
        {
            _ModelVar = p_copyAction._ModelVar;
        }

        public TA_Kill(XmlNode p_node, TriggerLoop p_parent)
            : base(p_parent)
        {
            InitActionVar(p_parent.ParentTrigger, ref _ModelVar, p_node.Attributes["Model"].Value);
        }

        public override void Activate(ref bool p_isRunNext)
        {
            p_isRunNext = true;
            ModelHuman model = GetModel(_ModelVar.ValueString);
            if (model != null)
            {
                model.StartPhysics();
            }
        }

        public override TriggerAction Copy()
        {
            return new TA_Kill(this);
        }

        public override string ToString()
        {
            return "Kill Model=" + _ModelVar.DebugStringValue;
        }
    }
}
