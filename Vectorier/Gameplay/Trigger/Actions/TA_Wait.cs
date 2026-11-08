#nullable disable warnings
using System.Xml;

namespace Vectorier.Gameplay.Trigger.Actions;

public class TA_Wait : TriggerAction
{
    private Variable _frameVar;

    private int _currentFrame;

    public override int Frames => _frameVar.ValueInt;

    private TA_Wait(TA_Wait copyAction)
        : base(copyAction._ParentLoop)
    {
        _currentFrame = copyAction._currentFrame;
        _frameVar = copyAction._frameVar;
    }

    public TA_Wait(XmlNode node, TriggerLoop parent)
        : base(parent)
    {
        InitActionVar(parent.ParentTrigger, ref _frameVar, node.Attributes["Frames"].Value);
    }

    public override void Activate(ref bool isRunNext)
    {
        if (_currentFrame > _frameVar.ValueInt)
        {
            isRunNext = true;
            _currentFrame = 0;
        }
        else
        {
            _currentFrame++;
            isRunNext = false;
        }
    }

    public override TriggerAction Copy()
    {
        return new TA_Wait(this);
    }

    public override void Reset()
    {
        _currentFrame = 0;
    }

    public override string ToString()
    {
        return "Wait Frames=" + _frameVar.DebugStringValue + " CurFrame=" + _currentFrame;
    }
}
