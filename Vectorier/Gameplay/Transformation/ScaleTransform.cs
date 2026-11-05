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
using System.Xml;
using Vectorier.Gameplay.Runners;
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Transformation
{
    public class SizeTransform : TransformPrototype
    {
        private int _Frames;

        private int _CurrentFrame;

        private float _FinalWidth;

        private float _FinalHeight;

        private List<Vector2> _Points = new List<Vector2>();

        public override int Frames => _Frames;

        public SizeTransform()
        {
            _Type = Type.Scale;
        }

        public static SizeTransform Create(XmlNode node)
        {
            if (node == null)
                return null;
            SizeTransform size = new SizeTransform();
            size.Parse(node);
            return size;
        }

        public override void Parse(XmlNode node)
        {
            _Frames = node.Attributes["Frames"].ParseInt();

            _FinalWidth = node.Attributes["FinalWidth"].ParseFloat(-1);
            _FinalHeight = node.Attributes["FinalHeight"].ParseFloat(-1);
        }

        protected void ReplaceZeroByEps(Vector2 p_point)
        {
            if (p_point.X == 0f)
            {
                p_point.X = 1E-05f;
            }
            if (p_point.Y == 0f)
            {
                p_point.Y = 1E-05f;
            }
        }

        public override bool Iteration()
        {
            if (_Runner == null || _Pause)
            {
                return false;
            }
            _Runner.TransformResize(_Points[_CurrentFrame]);
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
            _Points.Clear();
            var visualRunner = _Runner as VisualRunner;
            var quadRunner = _Runner as QuadRunner;
            if (visualRunner != null)
            {
                if (_FinalWidth < 0)
                {
                    _FinalWidth = visualRunner.Width;
                }
                if (_FinalHeight < 0)
                {
                    _FinalHeight = visualRunner.Height;
                }
                _Points.Add(new Vector2(visualRunner.Width, visualRunner.Height));
            }
            if (quadRunner != null)
            {
                if (_FinalWidth < 0)
                {
                    _FinalWidth = quadRunner.Width;
                }
                if (_FinalHeight < 0)
                {
                    _FinalHeight = quadRunner.Height;
                }
                _Points.Add(new Vector2(quadRunner.Width, quadRunner.Height));
            }
            _Points.Add(new Vector2(_FinalWidth, _FinalHeight));
            CalculatePoints();
        }

        private void CalculatePoints()
        {
            Vector2 point = _Points[0];
            Vector2 point2 = _Points[_Points.Count - 1];
            List<Vector2> list = new List<Vector2>();
            for (int i = 1; i < _Points.Count - 1; i++)
            {
                list.Add(_Points[i]);
            }
            _Points.Clear();
            float num = 0f;
            for (int j = 0; j < _Frames; j++)
            {
                double num2 = System.Math.Pow(1f - num, list.Count + 1);
                double num3 = num2 * point.X;
                double num4 = num2 * point.Y;
                int num5 = 0;
                for (num5 = 0; num5 < list.Count; num5++)
                {
                    num2 = (list.Count + 1) * System.Math.Pow(1f - num, list.Count - num5) * System.Math.Pow(num, num5 + 1);
                    num3 += num2 * list[num5].X;
                    num4 += num2 * list[num5].Y;
                }
                num2 = System.Math.Pow(num, num5 + 1);
                num3 += num2 * point2.X;
                num4 += num2 * point2.Y;
                _Points.Add(new Vector2((float)num3, (float)num4));
                num += 1f / _Frames;
            }
            _Points.Add(point2);
            ReplaceZeroByEps(_Points[_Points.Count - 1]);
            for (int num1 = _Points.Count - 1; num > 1; num--)
            {
                ReplaceZeroByEps(_Points[num1 - 1]);
                //_Points[num1].Round(100);
            }
            _Points.RemoveAt(0);
        }

        public override void Reset()
        {
            base.Reset();
            _CurrentFrame = 0;
        }

        public override TransformPrototype Clone()
        {
            var size = new SizeTransform();
            size._Frames = _Frames;
            size._FinalHeight = _FinalHeight;
            size._FinalWidth = _FinalWidth;
            return size;
        }
    }
}

