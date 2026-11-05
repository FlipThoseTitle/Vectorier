// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Transformation
{
    public abstract class TransformPrototype
    {
        protected Runner _Runner;

        protected bool _Pause;

        protected Type _Type;

        public virtual Runner Runner
        {
            get => _Runner;
            set => _Runner = value;
        }

        public bool Pause
        {
            get => _Pause;
            set => _Pause = value;
        }

        public Type Type => _Type;

        public bool IsMoveType => _Type == Type.Move;

        public abstract int Frames
        {
            get;
        }

        public abstract void Parse(XmlNode Node);

        public abstract bool Iteration();

        public abstract void CalcDelta();

        public abstract TransformPrototype Clone();

        public virtual void Reset()
        {
            _Pause = false;
        }
    }
}
