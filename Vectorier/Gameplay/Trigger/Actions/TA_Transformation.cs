#nullable disable warnings
using System;
using System.Xml;

namespace Vectorier.Gameplay.Trigger.Actions;

public class TA_Transformation : TriggerAction
{
    private Variable _nameVar;

    private Variable _pauseVar;

    private int _frame;

    private int _currentFrame;

    public override int Frames => _ParentLoop.ParentTrigger.ParentElements.Parent.GetTransformationFrame(_nameVar.ValueString);

    private TA_Transformation(TA_Transformation copyAction)
        : base(copyAction._ParentLoop)
    {
        _nameVar = copyAction._nameVar;
        _pauseVar = copyAction._pauseVar;
        _currentFrame = copyAction._currentFrame;
        _frame = copyAction._frame;
    }

    public TA_Transformation(XmlNode node, TriggerLoop parent, bool wait)
        : base(parent)
    {
        _currentFrame = -1;
        _frame = 0;
        InitActionVar(parent.ParentTrigger, ref _nameVar, node.Attributes["Name"].Value);
    }

    public override void Activate(ref bool isRunNext)
    {
        if (_currentFrame == -1)
        {
            Console.WriteLine("ACTIVATE TRANSFORM NAME: " + _nameVar.ValueString);
            _frame = _ParentLoop.ParentTrigger.ParentElements.Parent.RunTranformation(_nameVar.ValueString);
            _currentFrame = 0;
        }
        if (_currentFrame <= _frame)
        {
            _currentFrame++;
            isRunNext = false;
        }
        if (_currentFrame > _frame)
        {
            _currentFrame = -1;
            isRunNext = true;
        }
    }

    public override TriggerAction Copy()
    {
        return new TA_Transformation(this);
    }

    public override void Reset()
    {
        _frame = 0;
        _currentFrame = -1;
    }

    public override string ToString()
    {
        return "Transformation Name=" + _nameVar.DebugStringValue + (_pauseVar == null ? string.Empty : " Stop:1") + " Frames=" + _frame + " CurrentFrame=" + _currentFrame;
    }
}
