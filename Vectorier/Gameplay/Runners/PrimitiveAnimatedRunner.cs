#nullable disable warnings
using System.Collections.Generic;
using System.Numerics;
using Vectorier.Gameplay.Animation;
using Vectorier.Gameplay.Controllers;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Rl;

namespace Vectorier.Gameplay.Runners;

public class PrimitiveAnimatedRunner : PrimitiveRunner
{
    private ControllerAnimations _controllerAnimation;

    private AnimationReaction _reaction;

    public override bool IsStrike
    {
        get => false;
        set
        {
        }
    }

    public PrimitiveAnimatedRunner(int type, string name, Color color, Vector3 deltaPosition, float impulse, List<string> sounds)
        : base(type, name, color, deltaPosition, impulse, sounds)
    {
        Type = ModelType.PrimitiveAnimated;
    }

    public override void Load()
    {
        base.Load();
        _controllerAnimation = new ControllerAnimations(Model);
    }

    public override void Render(List<QuadRunner> platforms)
    {
        base.Render(platforms);
        _controllerAnimation.Render();
        if (_reaction != null)
        {
            Play();
        }
    }

    public void Play()
    {
        _controllerAnimation.Play(Animations.Animation[_reaction.Name], false, _reaction.FirstFrame);
        _reaction = null;
    }

    public void PlayReaction(string[] reactions)
    {
        _reaction = AnimationLoader.ParseReaction(reactions);
    }

    public override void Reset()
    {
        _controllerAnimation.Clear();
        base.Reset();
    }
}
