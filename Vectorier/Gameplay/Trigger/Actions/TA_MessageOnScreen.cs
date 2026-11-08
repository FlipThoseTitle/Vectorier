#nullable disable warnings
using System.Xml;
using Vectorier.Gameplay.Rl;
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Trigger.Actions;

public class TA_MessageOnScreen : TriggerAction
{
    public enum Animation
    {
        Fade
    }

    private Variable _textVariable;

    private Variable _timeVariable;

    private Color _color;

    private Animation _appearAnimation;

    private Animation _disappearAnimation;

    private TA_MessageOnScreen(TA_MessageOnScreen copyAction)
        : base(copyAction._ParentLoop)
    {
        _textVariable = copyAction._textVariable;
        _timeVariable = copyAction._timeVariable;
        _appearAnimation = copyAction._appearAnimation;
        _disappearAnimation = copyAction._disappearAnimation;
    }

    public TA_MessageOnScreen(XmlNode node, TriggerLoop parent)
        : base(parent)
    {
        _appearAnimation = Animation.Fade;
        _disappearAnimation = Animation.Fade;
        _color = ColorUtils.FromHex(node.Attributes["Color"].Value);
        InitActionVar(parent.ParentTrigger, ref _textVariable, node.Attributes["Text"].Value);
        InitActionVar(parent.ParentTrigger, ref _timeVariable, node.Attributes["Frames"].Value);
    }

    public override void Activate(ref bool isRunNext)
    {
        isRunNext = true;
        GameplayUI.ShowMessage(_textVariable.ValueString, _timeVariable.ValueInt, _color);
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
