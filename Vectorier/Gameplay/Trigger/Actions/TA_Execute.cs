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
    public class TA_Execute : TriggerAction
    {
        public TA_Execute(TA_Execute p_copyAction)
            : base(p_copyAction._ParentLoop)
        {
        }

        public TA_Execute(XmlNode p_node, TriggerLoop p_parent)
            : base(p_parent)
        {
        }

        public override void Activate(ref bool p_isRunNext)
        {
            p_isRunNext = true;
            var elements = _ParentLoop.ParentTrigger.ParentElements;
            foreach (var particle in elements.Particles)
            {
                particle.PlayAnimation(GetModel().GetNode());
            }
            foreach (var animation in elements.Animations)
            {
                animation.PlayAnimation();
            }
            foreach (var visual in elements.Visuals)
            {
                if (visual.IsVanishing)
                {
                    visual.IsEnabled = false;
                }
            }
        }

        public override TriggerAction Copy()
        {
            return new TA_Execute(this);
        }

        public override void Reset()
        {
            foreach (var visual in _ParentLoop.ParentTrigger.ParentElements.Visuals)
            {
                if (visual.IsVanishing)
                {
                    visual.IsEnabled = true;
                }
            }
        }

        public override string ToString()
        {
            return "Execute";
        }
    }
}
