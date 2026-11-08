#nullable disable warnings
using System.Xml;

namespace Vectorier.Gameplay.Trigger.Actions;

public class TA_Sound : TriggerAction
{
    private Variable _soundVar;

    private TA_Sound(TA_Sound copyAction)
        : base(copyAction._ParentLoop)
    {
        _soundVar = copyAction._soundVar;
    }

    public TA_Sound(XmlNode node, TriggerLoop parent)
        : base(parent)
    {
        InitActionVar(parent.ParentTrigger, ref _soundVar, node.Attributes["Name"].Value);
    }

    public override void Activate(ref bool isRunNext)
    {
        isRunNext = true;
        var sounds = _soundVar.ValueString.Split('|');
        SoundsManager.PlaySounds(sounds[Random.Range(0, sounds.Length - 1)]);
    }

    public override TriggerAction Copy()
    {
        return new TA_Sound(this);
    }

    public override string ToString()
    {
        return "Sound:" + _soundVar.DebugStringValue;
    }
}
