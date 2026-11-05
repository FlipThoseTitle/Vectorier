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
using Vectorier.Gameplay.Animation;
using Vectorier.Gameplay.Animation.Events;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Controllers
{
    public class ControllerKeys
    {
        private readonly int _interval;

        private int _onInterval;

        private readonly ModelHuman _model;

        public KeyVariables KeyVariables
        {
            get;
            private set;
        }

        public bool Enable
        {
            get;
            set;
        }

        public ControllerKeys(ModelHuman model)
        {
            Enable = true;
            _model = model;
            _interval = 21;
            _onInterval = 0;
        }

        public void SetKeyVariable(KeyVariables p_value)
        {
            if (!Enable)
            {
                return;
            }
            KeyVariables = p_value;
            _onInterval = 0;
            if (TutorialAreaRunner.current != null && !TutorialAreaRunner.current.OnKeyClick(p_value))
            {
                return;

            }

            if (TrickAreaRunner.Current != null)
            {
                if (!TrickAreaRunner.Current.isActive)
                {
                    if (_model.IsTrick)
                    {
                        if (p_value.IsEqual(KeyboardKey.Up))
                        {
                            //LevelMainController.current.levelSceneController.TrickNotBuy(TrickAreaRunner.Current);
                        }
                    }
                }
            }
            _model.ControllerTrigger.SetKeyEvent(p_value.ToString());
        }

        public void SetKeyVariable_force(KeyVariables p_value)
        {
            _onInterval = 0;
            KeyVariables = p_value;
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

        public List<AnimationReaction> GetValidateReactions(KeyVariables p_value, AnimationEventKey p_anim_key, int p_sign)
        {
            if (p_value == null)
            {
                return null;
            }
            if (p_anim_key.IsKey(p_value, p_sign))
            {
                return p_anim_key.Reaction;
            }
            return null;
        }

        public void Reset()
        {
            KeyVariables = null;
            Enable = true;
        }
    }
}
