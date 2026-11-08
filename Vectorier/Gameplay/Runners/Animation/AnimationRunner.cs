#nullable disable warnings
using System.Collections.Generic;
using Vectorier.Gameplay.Level;

namespace Vectorier.Gameplay.Runners.Animation;

public class AnimationRunner : AnimationRendering
{
    private Dictionary<string, string> _oldAnimationNames = new Dictionary<string, string>
    {
        {"p_glass1_mini", "glass_1" },
        {"p_birds1", "bird_v0" },
        { "p_birds2", "bird_v2"},
        { "p_birds3", "bird_v3" },
        {"p_paper1", "paper_v1" }
    };

    public AnimationRunner(float x, float y, float width, float height, string name, int type, float scaleX, float scaleY, bool replay = false, int speed = 1)
        : base(name, x, y, width, height, replay, 2)
    {
        TypeClass = RunnerType.Animation;
        LocalScale = new System.Numerics.Vector2(scaleX, scaleY);


        Z = VisualRunner.SortingOrder * -0.0001f - 1;

        VisualRunner.SortingOrder++;
    }

    public override void Init(float pivotX = 0, float pivotY = 0)
    {
        if (_oldAnimationNames.ContainsKey(Name))
        {
            Name = _oldAnimationNames[Name];
        }
        base.Init(pivotX, pivotY);
        IsEnabled = false;
    }

    public override void Reset()
    {
        Stop();
        base.Reset();
    }

    public virtual void PlayAnimation()
    {
        _IsPlay = true;
        RunnerRender.AddRunner(this);
        PlayFrom(0);
        IsEnabled = true;
    }

    public override void Stop(bool Value = false)
    {
        _IsPlay = false;
        IsEnabled = false;
    }
}
