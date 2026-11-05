// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vectorier.Gameplay.Level;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Runners;
using Vectorier.Gameplay.Trigger.Events;

namespace Vectorier.Gameplay.Trigger
{
    public class TriggerTimer
    {
        private bool _IsActiv;

        private int _Counter;

        private TriggerRunner _Parent;

        public TriggerTimer(TriggerRunner p_parent)
        {
            _Parent = p_parent;
        }

        public void Start(int p_value)
        {
            RunnerRender.AddRunner(_Parent);
            _Counter = p_value;
            _IsActiv = true;
        }

        public bool Render()
        {
            if (_IsActiv)
            {
                _Counter--;
                if (_Counter <= 0)
                {
                    _IsActiv = false;
                    TE_Timeout p_event = new TE_Timeout();
                    ModelHuman modelByName = LevelMainController.current.Location.GetModelByName(_Parent.ModelVar.ValueString);
                    _Parent.CheckEvent(p_event, modelByName);
                    return !_IsActiv;
                }
                return false;
            }
            return true;
        }

        public void Reset()
        {
            _IsActiv = false;
        }
    }
}
