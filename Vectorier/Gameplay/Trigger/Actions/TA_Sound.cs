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
    public class TA_Sound : TriggerAction
    {
        private Variable _SoundVar;

        private TA_Sound(TA_Sound p_copyAction)
            : base(p_copyAction._ParentLoop)
        {
            _SoundVar = p_copyAction._SoundVar;
        }

        public TA_Sound(XmlNode p_node, TriggerLoop p_parent)
            : base(p_parent)
        {
            InitActionVar(p_parent.ParentTrigger, ref _SoundVar, p_node.Attributes["Name"].Value);
        }

        public override void Activate(ref bool p_isRunNext)
        {
            p_isRunNext = true;
            var sounds = _SoundVar.ValueString.Split('|');
            SoundsManager.PlaySounds(sounds[Random.Range(0, sounds.Length - 1)]);
        }

        public override TriggerAction Copy()
        {
            return new TA_Sound(this);
        }

        public override string ToString()
        {
            return "Sound:" + _SoundVar.DebugStringValue;
        }
    }
}
