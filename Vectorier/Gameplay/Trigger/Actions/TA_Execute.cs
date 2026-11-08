#nullable disable warnings
using System.Xml;

namespace Vectorier.Gameplay.Trigger.Actions;

public class TA_Execute : TriggerAction
{
    public TA_Execute(TA_Execute copyAction)
        : base(copyAction._ParentLoop)
    {
    }

    public TA_Execute(XmlNode node, TriggerLoop parent)
        : base(parent)
    {
    }

    public override void Activate(ref bool isRunNext)
    {
        isRunNext = true;
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
