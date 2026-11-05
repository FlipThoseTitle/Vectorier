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
using System.Numerics;
using Vectorier.Gameplay.Models;

namespace Vectorier.Gameplay.Runners
{
    public abstract class ItemRunner : Runner
    {
        protected int _Type;

        protected float _Radius = 80;

        protected bool _inclusion;

        protected Color _DefaultColor = Color.White;

        protected Color _CurrentColor = Color.White;

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
}
