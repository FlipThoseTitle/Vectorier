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
    public class TA_Spawn : TriggerAction
    {
        private Variable _SpawnVar;

        private Variable _ModelVar;

        private TA_Spawn(TA_Spawn p_copyAction)
            : base(p_copyAction._ParentLoop)
        {
            _SpawnVar = p_copyAction._SpawnVar;
            _ModelVar = p_copyAction._ModelVar;
        }

        public TA_Spawn(XmlNode p_node, TriggerLoop p_parent)
            : base(p_parent)
        {
            InitActionVar(p_parent.ParentTrigger, ref _ModelVar, p_node.Attributes["Model"].Value);
            if (p_node.Attributes["Spawn"] != null)
            {
                InitActionVar(p_parent.ParentTrigger, ref _SpawnVar, p_node.Attributes["Spawn"].Value);
            }
        }

        public override void Activate(ref bool p_isRunNext)
        {
            p_isRunNext = true;
            var model = GetModel(_ModelVar.ValueString);
            model.Reset();
            model.IsVisible = true;
            model.IsDelayEnd = true;
            model.Play(_ParentLoop.ParentTrigger.GetSpawnByName(_SpawnVar.ValueString));
        }

        public override TriggerAction Copy()
        {
            return new TA_Spawn(this);
        }

        public override string ToString()
        {
            return "Spawn Model=" + _ModelVar.DebugStringValue + " Spawn" + _SpawnVar == null ? "null" : _SpawnVar.DebugStringValue;
        }
    }
}
