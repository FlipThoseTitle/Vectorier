#nullable disable warnings
using System;
using System.Numerics;
using Vectorier.Gameplay.Models;

namespace Vectorier.Gameplay.Runners;

public abstract class ItemRunner : Runner
{
    protected int _Type;

    protected float _Radius = 80;

    protected bool _inclusion;

    public int Type => _Type;

    public override bool Render()
    {
        return true;
    }

    public abstract void Init();

    public ItemRunner(float x, float y, int type)
        : base(x, y)
    {
        _Type = type;
        TypeClass = RunnerType.Platform;
    }

    public virtual void Collision(ModelHuman modelHuman)
    {
        if (!_inclusion)
        {
            if (!modelHuman.IsBot)
            {
                if (Inside(modelHuman.ModelObject.CenterOfMassNode.Start))
                {
                    Play();
                    _inclusion = true;
                    AddBonus(modelHuman);
                }
            }
        }
    }

    public virtual void AddBonus(ModelHuman modelHuman)
    {
    }

    public bool Inside(Vector3 vector)
    {
        return Math.Sqrt((LocalPosition.X - vector.X) * (LocalPosition.X - vector.X) + (LocalPosition.Y - vector.Y) * (LocalPosition.Y - vector.Y)) < _Radius;
    }

    public override void Reset()
    {
        base.Reset();
        _inclusion = false;
    }

    public virtual void Play()
    {
    }
}
