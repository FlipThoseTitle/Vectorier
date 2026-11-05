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
    public class TA_EndGame : TriggerAction
    {
        private Variable _TimeVar;

        private Variable _ResultVar;

        private Variable _ModelVar;

        private TA_EndGame(TA_EndGame p_copyAction)
            : base(p_copyAction._ParentLoop)
        {
            _ResultVar = p_copyAction._ResultVar;
            _TimeVar = p_copyAction._TimeVar;
            _ModelVar = p_copyAction._ModelVar;
        }

        public TA_EndGame(XmlNode p_node, TriggerLoop p_parent)
            : base(p_parent)
        {
            string value = p_node.Attributes["Result"].Value;
            string p_nameOrValue = "80";
            if (p_node.Attributes["Frames"] != null)
            {
                p_nameOrValue = p_node.Attributes["Frames"].Value;
            }
            string value2 = p_node.Attributes["Model"].Value;
            InitActionVar(p_parent.ParentTrigger, ref _ResultVar, value);
            InitActionVar(p_parent.ParentTrigger, ref _TimeVar, p_nameOrValue);
            InitActionVar(p_parent.ParentTrigger, ref _ModelVar, value2);
        }

        public override void Activate(ref bool p_isRunNext)
        {
            p_isRunNext = true;
            string valueString = _ModelVar.ValueString;
            switch (_ResultVar.ValueString)
            {
                case "Win":
                    LevelMainController.current.Win(GetModel(valueString), _TimeVar.ValueInt / 60f);
                    break;
                case "Loss":
                    LevelMainController.current.Loss(GetModel(valueString), _TimeVar.ValueInt / 60f);
                    break;
                case "Death":
                    LevelMainController.current.Death(GetModel(valueString), _TimeVar.ValueInt / 60f);
                    break;
            }
        }

        public override TriggerAction Copy()
        {
            return new TA_EndGame(this);
        }

        public override string ToString()
        {
            return "EndGame Model=" + _ModelVar.DebugStringValue + " Result=" + _ResultVar.DebugStringValue + " Frames" + _TimeVar.DebugStringValue;
        }
    }
}
