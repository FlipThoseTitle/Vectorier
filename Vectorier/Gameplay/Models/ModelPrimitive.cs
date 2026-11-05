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
using Vectorier.Gameplay.Node;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Models
{
    public class ModelPrimitive : Model
    {
        private bool _IsStrike;

        private List<string> _Sounds;

        private PrimitiveRunner _Base;

        public float Impulse
        {
            get;
            set;
        }

        public int RenderTime
        {
            get;
            set;
        }

        public int RenderCount
        {
            get;
            set;
        }

        public virtual bool IsStrike
        {
            get => _IsStrike;
            set
            {
                _IsStrike = value;
                if (value)
                {
                    _ControllerPhysics.Start();
                    RenderTime = RenderCount;
                }
            }
        }

        public override Rectangle Rectangle
        {
            get
            {
                var rectangle = base.Rectangle;
                var node = _ModelObject.GetNode();
                rectangle.Origin = rectangle.Origin.Add(node.Start.X, node.Start.Y);
                return rectangle;
            }
        }

        public bool IsCollisible => _ControllerPhysics.IsPhysics ? RenderCount == 0 : false;

        public ModelPrimitive(PrimitiveRunner primitiveRunner, List<string> skins, List<string> sounds)
            : base(skins, ModelType.Primitive)
        {
            _Sounds = sounds;
            _Base = primitiveRunner;
            RenderCount = 150;
            Init();
        }

        public override void Strike(ModelLine edge, Vector3 point, Vector3 impulse)
        {
            IsStrike = true;
            PlaySound();
            _controllerStrike.Striking(edge, point, impulse * Impulse);
        }

        private void PlaySound()
        {
            int num = Random.Range(0, _Sounds.Count);
            SoundsManager.PlaySounds(_Sounds[num]);
        }
    }
}
