#nullable disable warnings
using Vectorier.Gameplay.Controllers;
using Vectorier.Gameplay.Models;

namespace Vectorier.Gameplay.Runners;

//FIX
public class TutorialAreaRunner : AreaRunner
{
    private uint _repeatCounter;
    private string _key;

    public KeyVariables Key { get; private set; }

    public string Description { get; private set; }

    public static TutorialAreaRunner current { get; private set; }

    private bool ShowTutorial
    {
        get
        {
            if (_repeatCounter < 2)
            {
                return true;
            }
            return false;
        }
    }

    public TutorialAreaRunner(float x, float y, float width, float height, string typeName, string name, string key, string description)
        : base(AreaType.Help, x, y, width, height, typeName, name)
    {
        _key = key;
        Key = new KeyVariables(key);
        Description = description;
    }

    public override bool OnKeyClick(KeyVariables keyVariables)
    {
        if (Key.IsEqual(keyVariables))
        {
            LevelMainController.current.TutorialUnLockGame();
            current = null;
            _repeatCounter++;
            return true;
        }
        return false;
    }

    public override void Activate(ModelHuman model)
    {
        if (ShowTutorial)
        {
            if (LevelMainController.current.IsDeath || model.IsBot || !model.ControllerAnimations.IsActionInterval)
            {
                return;
            }
            current = this;
            LevelMainController.current.TutorialAreaActivate(this);
        }
    }

    public override void Deactivate(ModelHuman model)
    {
        base.Deactivate(model);
        if (current == null || !ShowTutorial)
        {
            return;
        }
        LevelMainController.current.TutorialLockGame();
    }
}
