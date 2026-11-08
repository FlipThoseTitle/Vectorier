#nullable disable warnings
using System.Collections.Generic;
using Vectorier.Gameplay.Animation;
using Vectorier.Gameplay.Animation.Events;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Rl;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Controllers;

public class ControllerKeys
{
    private readonly int _interval;

    private int _onInterval;

    private readonly ModelHuman _model;

    public KeyVariables KeyVariables { get; private set; }

    public bool Enable { get; set; }

    public ControllerKeys(ModelHuman model)
    {
        Enable = true;
        _model = model;
        _interval = 21;
        _onInterval = 0;
    }

    public void SetKeyVariable(KeyVariables value)
    {
        if (!Enable)
        {
            return;
        }
        KeyVariables = value;
        _onInterval = 0;
        if (TutorialAreaRunner.current != null && !TutorialAreaRunner.current.OnKeyClick(value))
        {
            return;

        }

        if (TrickAreaRunner.Current != null)
        {
            if (!TrickAreaRunner.Current.isActive)
            {
                if (_model.IsTrick)
                {
                    if (value.IsEqual(KeyboardKey.Up))
                    {
                    }
                }
            }
        }
        _model.ControllerTrigger.SetKeyEvent(value.ToString());
    }

    public void SetKeyVariable_force(KeyVariables value)
    {
        _onInterval = 0;
        KeyVariables = value;
    }

    public void ClearAnimation()
    {
        KeyVariables = null;
    }

    public void Render()
    {
        if (KeyVariables != null)
        {
            if (_onInterval >= _interval)
            {
                ClearAnimation();
            }
            else
            {
                _onInterval++;
            }
        }
    }

    public List<AnimationReaction> GetValidateReactions(KeyVariables value, AnimationEventKey anim_key, int sign)
    {
        if (value == null)
        {
            return null;
        }
        if (anim_key.IsKey(value, sign))
        {
            return anim_key.Reaction;
        }
        return null;
    }

    public void Reset()
    {
        KeyVariables = null;
        Enable = true;
    }
}
