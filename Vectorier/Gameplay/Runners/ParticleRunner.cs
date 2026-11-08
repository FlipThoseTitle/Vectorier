#nullable disable warnings
using System;
using System.Numerics;
using Vectorier.Gameplay.Controllers;
using Vectorier.Gameplay.Level;
using Vectorier.Gameplay.Node;
using Vectorier.Gameplay.Runners.Animation;

namespace Vectorier.Gameplay.Runners;

//FIX
public class ParticleRunner : AnimationRendering
{
    private int _liveCount;

    private int _liveTime;

    private bool _isRender;

    private ModelNode? _node;

    public ParticleRunner(float x, float y, float width, float height, string name)
        : base(name, x, y, width, height, true)
    {
        _isRender = false;
        TypeClass = RunnerType.Particle;
        _IsPlay = false;
        _liveTime = (int)(Random.Range(0.5f, 1.5f) * 60);

        Z = -11;

    }

    public override void Init(float pivotX = 0.5f, float pivotY = 0.5f)
    {
        if (Name == "p_glass1_mini")
        {
            Name = "glass_1";
        }
        base.Init(pivotX, pivotY);
        LocalScale *= Random.Range(0.5f, 1f);

        Animator.Color = Vectorier.Gameplay.Rl.Color.Black;
    }

    public void PlayAnimation(ModelNode node)
    {
        RunnerRender.AddRunner(this);
        _liveCount = 0;
        _IsPlay = true;
        _isRender = true;
        _node = new ModelNode(new Vector3());
        _node.Attenuation = 0;
        _node.Radius = 10;
        _node.PositionStart(LocalPosition.X + _Width * 0.5f, LocalPosition.Y + _Height * 0.5f, 0);
        _node.EndAssignStart();
        MoveParticle();
        SetImpuls(node);
        IsEnabled = true;
        PlayFrom(Random.Range(0, _TotalFrames));
    }

    private void SetImpuls(ModelNode node)
    {
        var nodeStart = _node.Start;
        var start = node.Start;
        var end = node.End;

        var vector = end - start;
        vector = vector.Normalize();
        vector = vector.Multiply(100);

        var vector2 = end + vector;
        vector2 = nodeStart - vector2;
        vector2 = vector2.Normalize();
        vector2 = vector2.Multiply(10 / MathF.Sqrt(LevelMainController.current.slowModeFrames));
        vector2 += nodeStart;

        var vector3 = new Vector3(Random.Range(-5, 5), Random.Range(-5, 5), 0);
        vector2 += vector3;
        _node.PositionStart(vector2);
    }

    private void MoveParticle()
    {
        LocalPosition = new Vector2(_node.Start.X, _node.Start.Y);
    }

    public override bool Render()
    {
        if (!_isRender)
        {
            return true;
        }
        base.Render();
        _node.TimeStep(ControllerPhysics.Gravity);
        ControllerCollisions.PushingNode(_node, Sets.Current.Quads, 0.5f);
        MoveParticle();
        IsCollision();
        return false;
    }

    private void IsCollision()
    {
        _liveCount++;
        if (_liveCount < _liveTime)
        {
            return;
        }
        Stop(true);
        _isRender = false;
        IsEnabled = false;
    }

    public override void Reset()
    {
        base.Reset();
        _node = null;
        Stop();
        IsEnabled = false;
        _IsPlay = false;
        _isRender = false;

        LocalScale *= Random.Range(0.5f, 1f);
    }
}
