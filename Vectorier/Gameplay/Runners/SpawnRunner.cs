#nullable disable warnings
using Vectorier.Gameplay.Animation;

namespace Vectorier.Gameplay.Runners;

public class SpawnRunner : Runner
{
    protected string _animations;

    public AnimationReaction Reaction;

    public SpawnRunner()
        : base(0f, 0f)
    {
        TypeClass = RunnerType.Spawn;
    }

    public SpawnRunner(float x, float y, string n, string animation)
        : base(x, y)
    {
        _animations = animation;
        TypeClass = RunnerType.Spawn;
        Name = n;
        Hash = n.GetHashCode();
        if (string.IsNullOrEmpty(_animations))
        {
            return;
        }
        Reaction = AnimationLoader.ParseReaction(_animations.Split('|'));
    }

    public override bool Render()
    {
        return true;
    }
}
