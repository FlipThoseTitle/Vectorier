// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vectorier.Gameplay.Rl;
using System.Xml;
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Trigger.Actions
{
    public class TA_MessageOnScreen : TriggerAction
    {
        public enum Animation
        {
            Fade
        }

        private Variable _TextVariable;

        private Variable _TimeVariable;

        private Color _Color;

        private Animation _AppearAnimation;

        private Animation _DisappearAnimation;

        private static Animation GetTypeByString(string value)
        {
            return default(Animation);
        }

        private TA_MessageOnScreen(TA_MessageOnScreen p_copyAction)
            : base(p_copyAction._ParentLoop)
        {
            _TextVariable = p_copyAction._TextVariable;
            _TimeVariable = p_copyAction._TimeVariable;
            _AppearAnimation = p_copyAction._AppearAnimation;
            _DisappearAnimation = p_copyAction._DisappearAnimation;
        }

        public TA_MessageOnScreen(XmlNode p_node, TriggerLoop p_parent)
            : base(p_parent)
        {
            _AppearAnimation = Animation.Fade;
            _DisappearAnimation = Animation.Fade;
            _Color = ColorUtils.FromHex(p_node.Attributes["Color"].Value);
            InitActionVar(p_parent.ParentTrigger, ref _TextVariable, p_node.Attributes["Text"].Value);
            InitActionVar(p_parent.ParentTrigger, ref _TimeVariable, p_node.Attributes["Frames"].Value);
        }

        public override void Activate(ref bool p_isRunNext)
        {
            p_isRunNext = true;
            GameplayUI.ShowMessage(_TextVariable.ValueString, _TimeVariable.ValueInt, _Color);
        }

        public override TriggerAction Copy()
        {
            return new TA_MessageOnScreen(this);
        }

        public override string ToString()
        {
            return "MessageOnScreen";
        }
    }
}
