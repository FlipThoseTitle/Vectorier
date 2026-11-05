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
using Math = System.Math;

namespace Vectorier.Gameplay.Transformation
{
    public class MoveInterval
    {
        private int _Number;

        private int _Frames;

        private float _Delay;

        private float _CurrentDelay;

        private float _CurrentDelayFake;

        private List<Vector2> _Points = new List<Vector2>();

        private int _CurrentFrame;

        private int _CurrentFrameFake;

        public int Number
        {
            get => _Number;
            set => _Number = value;
        }

        public int Frames
        {
            get => _Frames;
            set => _Frames = value;
        }

        public float Delay
        {
            get => _Delay;
            set
            {
                _Delay = value;
                _CurrentDelay = value;
            }
        }

        public List<Vector2> Points => _Points;

        public int CurrentFrame => _CurrentFrame;

        public MoveInterval()
        {
        }

        public MoveInterval(int number, int frames, float delay)
        {
            _Number = number;
            _Frames = frames;
            _CurrentFrame = 0;
            _Delay = delay;
            _CurrentDelay = _Delay;
        }

        public void Reset()
        {
            _CurrentFrame = 0;
            _CurrentDelay = _Delay;
        }

        public void Init()
        {
        }

        public void CalculateExtraPoints()
        {
            Vector2 vector3f = _Points[0];
            Vector2 vector3f2 = _Points[_Points.Count - 1];
            List<Vector2> list = new List<Vector2>();
            for (int i = 1; i < _Points.Count - 1; i++)
            {
                list.Add(_Points[i]);
            }
            _Points.Clear();
            float num = 0f;
            for (int j = 0; j < _Frames; j++)
            {
                double num2 = Math.Pow(1f - num, list.Count + 1);
                double num3 = num2 * (double)vector3f.X;
                double num4 = num2 * (double)vector3f.Y;
                int num5 = 0;
                for (num5 = 0; num5 < list.Count; num5++)
                {
                    num2 = (double)(list.Count + 1) * Math.Pow(1f - num, list.Count - num5) * Math.Pow(num, num5 + 1);
                    num3 += num2 * (double)list[num5].X;
                    num4 += num2 * (double)list[num5].Y;
                }
                num2 = Math.Pow(num, num5 + 1);
                num3 += num2 * (double)vector3f2.X;
                num4 += num2 * (double)vector3f2.Y;
                _Points.Add(new Vector2((int)num3, (int)num4));
                num += 1f / (float)_Frames;
            }
            _Points.Add(vector3f2);

            for (int num2 = _Points.Count - 1; num2 > 0; num2--)
            {
                var v = _Points[num2];
                v = v.Subtract(_Points[num2 - 1]);
                v = v.Round(100);
                _Points[num2] = v;
            }
            _Points.RemoveAt(0);
        }


        public bool Iteration(Runner runner)
        {

            if (_CurrentDelay <= 0)
            {
                runner.UpdatePosition(_Points[_CurrentFrame]);

                _CurrentFrame++;
                if (_CurrentFrame < _Points.Count)
                {
                    return true;
                }
                _CurrentFrame = 0;
                _CurrentDelay = _Delay;
                return false;
            }
            runner.TweenPosition = runner.TweenPosition.Reset();
            _CurrentDelay--;
            return true;
        }

        public bool InerationFake(ref Vector2 p_point, ref Vector2 p_velocity, bool p_isInit)
        {
            if (p_isInit)
            {
                _CurrentDelayFake = _CurrentDelay;
                _CurrentFrameFake = _CurrentFrame;
            }
            if (_CurrentDelayFake <= 0)
            {
                var currentPoint = _Points[_CurrentFrameFake];
                if (currentPoint != null && p_velocity != null)
                {
                    p_velocity = p_velocity.Set(currentPoint.X, currentPoint.Y);
                    p_point = p_point.Add(currentPoint);
                    _CurrentFrameFake++;
                    if (_CurrentFrameFake < _Points.Count)
                    {
                        return true;
                    }
                    _CurrentDelayFake = _Delay;
                    _CurrentFrameFake = 0;
                    return false;
                }
            }
            _CurrentDelayFake--;
            return true;
        }

        public static MoveInterval Create(XmlNode node)
        {
            var moveInterval = new MoveInterval();
            moveInterval._Number = node.Attributes["Number"].ParseInt();
            moveInterval._Frames = node.Attributes["FramesToMove"].ParseInt();
            var delay = node.Attributes["Delay"].ParseFloat();
            moveInterval._Delay = delay;
            moveInterval._CurrentDelay = delay;
            foreach (XmlNode node2 in node.ChildNodes)
            {
                var point = VectorUtils.Create(node2);
                if (point.HasValue)
                {
                    moveInterval._Points.Add(point.Value);
                }
            }
            moveInterval.CalculateExtraPoints();
            return moveInterval;
        }

        public int DeltaBySizeAndCurrentPoint()
        {
            return _Points.Count + ~_CurrentFrame;
        }

        public MoveInterval Clone()
        {
            var clone = new MoveInterval(_Number, _Frames, _Delay);
            foreach (var point in _Points)
            {
                var c = VectorUtils.Clone(point);
                clone._Points.Add(c);
            }
            return clone;
        }
    }
}
