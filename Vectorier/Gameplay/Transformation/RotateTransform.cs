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
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Transformation
{
    public class RotateTransform : TransformPrototype
    {
        private int _Frames;

        private int _CurrentFrame;

        List<float> _Angles = new List<float>();

        public override int Frames => _Frames;

        public RotateTransform()
        {
            _Type = Type.Rotate;
        }

        public static RotateTransform Create(XmlNode node)
        {
            if (node == null)
                return null;
            RotateTransform rotate = new RotateTransform();
            rotate.Parse(node);

            return rotate;
        }

        public override void Parse(XmlNode node)
        {
            _Frames = node.Attributes["Frames"].ParseInt();
            float num = node.Attributes["Angle"].ParseFloat();
            CalcLinear(num);
        }

        private void CalcLinear(float p_angel)
        {
            float item = p_angel / _Frames;
            for (int i = 0; i < _Frames; i++)
            {
                _Angles.Add(item);
            }
        }

        public override bool Iteration()
        {

            if (_Runner == null || _Pause)
            {
                return false;
            }
            _Runner.TransformRotate(_Angles[_CurrentFrame]);
            _CurrentFrame++;
            if (_CurrentFrame >= _Frames)
            {
                _Pause = true;
                return false;
            }
            return true;
        }

        public override void CalcDelta()
        {
        }

        public override void Reset()
        {
            base.Reset();
            _CurrentFrame = 0;
        }

        public override TransformPrototype Clone()
        {
            var rotate = new RotateTransform();
            rotate._Frames = _Frames;
            foreach (var angle in _Angles)
            {
                rotate._Angles.Add(angle);
            }
            return rotate;
        }
    }
}

