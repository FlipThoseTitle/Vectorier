// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Numerics;
using Vectorier.Gameplay.Controllers;
using Vectorier.Gameplay.Level;
using Vectorier.Gameplay.Node;
using Vectorier.Gameplay.Runners.Animation;

namespace Vectorier.Gameplay.Runners
{
    //FIX
    public class ParticleRunner : AnimationRendering
    {
        private int _liveCount;

        private int _liveTime;

        private int _impuls;

        private bool _isRender;

        private ModelNode? _node;

        public ParticleRunner(float p_x, float p_y, float p_width, float p_height, string p_name)
            : base(p_name, p_x, p_y, p_width, p_height, true)
        {
            _isRender = false;
            TypeClass = RunnerType.Particle;
            _impuls = 10;
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

        public void PlayAnimation(ModelNode p_node)
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
            SetImpuls(p_node);
            IsEnabled = true;
            PlayFrom(Random.Range(0, _TotalFrames));
        }

        private void SetImpuls(ModelNode p_node)
        {
            var nodeStart = _node.Start;
            var start = p_node.Start;
            var end = p_node.End;

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
}
