#nullable disable warnings
using Vectorier.Gameplay.Level;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Runners;
using Vectorier.Gameplay.Trigger.Events;

namespace Vectorier.Gameplay.Trigger;

public class TriggerTimer
{
    private bool _isActiv;

    private int _counter;

    private TriggerRunner _parent;

    public TriggerTimer(TriggerRunner parent)
    {
        _parent = parent;
    }

    public void Start(int value)
    {
        RunnerRender.AddRunner(_parent);
        _counter = value;
        _isActiv = true;
    }

    public bool Render()
    {
        if (_isActiv)
        {
            _counter--;
            if (_counter <= 0)
            {
                _isActiv = false;
                TE_Timeout p_event = new TE_Timeout();
                ModelHuman modelByName = LevelMainController.current.Location.GetModelByName(_parent.ModelVar.ValueString);
                _parent.CheckEvent(p_event, modelByName);
                return !_isActiv;
            }
            return false;
        }
        return true;
    }

    public void Reset()
    {
        _isActiv = false;
    }
}
