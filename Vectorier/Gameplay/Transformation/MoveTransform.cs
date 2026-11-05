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
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Transformation
{
    public class MoveTransform : TransformPrototype
    {
        private int _Frames;

        private int _Loop;

        private int _LoopFromInterval;

        private int _CurrentInterval;

        private List<MoveInterval> _Intervals = new List<MoveInterval>();

        public override int Frames => _Frames;

        public MoveTransform()
        {
            _Type = Type.Move;
        }

        public static MoveTransform Create(XmlNode p_node)
        {
            if (p_node == null) return null;
            var move = new MoveTransform();
            move.Parse(p_node);
            return move;
        }

        public override void Parse(XmlNode p_node)
        {
            _Loop = p_node.Attributes["Cycle"].ParseInt();
            _LoopFromInterval = p_node.Attributes["CycleFromInterval"].ParseInt();
            foreach (XmlNode node in p_node.ChildNodes)
            {
                if (node.Name == "MoveInterval")
                {
                    var interval = MoveInterval.Create(node);
                    _Frames += interval.Frames;
                    _Intervals.Add(interval);
                }
            }
        }

        public override bool Iteration()
        {

            if (_Runner == null || _Pause)
            {
                return false;
            }

            if (_Intervals.Count <= _CurrentInterval)
            {
                if (_Loop == 0)
                {
                    if (_Loop != -1)
                    {
                        _Runner.TweenPosition = _Runner.TweenPosition.Reset();
                        _Pause = true;
                        return false;
                    }
                }
                else
                {
                    _Loop--;
                }
                _CurrentInterval = _LoopFromInterval;
            }

            if (!_Intervals[_CurrentInterval].Iteration(_Runner))
            {
                _CurrentInterval++;
            }
            return true;
        }

        public void GetNextIterationPoint(ref Vector2 p_point, ref Vector2 p_velocity, int p_step)
        {
            if (p_step > 0)
            {
                for (int i = 0; i < p_step; i++)
                {
                    if (_Intervals.Count <= _CurrentInterval)
                    {
                        if (_Loop < 1 && _Loop != -1)
                        {
                            return;
                        }
                    }
                    _Intervals[_CurrentInterval].InerationFake(ref p_point, ref p_velocity, i == 0);
                }
            }
        }


        public override void Reset()
        {
            _Pause = false;
            _CurrentInterval = 0;
            foreach (var interval in _Intervals)
            {
                interval.Reset();
            }
        }

        public override TransformPrototype Clone()
        {
            var move = new MoveTransform();
            move._Loop = _Loop;
            _LoopFromInterval = 0;
            move._LoopFromInterval = _LoopFromInterval;
            foreach (var i in _Intervals)
            {
                move._Intervals.Add(i.Clone());
            }
            return move;
        }

        public override void CalcDelta()
        {
        }
    }
}
