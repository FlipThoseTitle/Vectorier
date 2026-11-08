#nullable disable warnings
using System.Collections.Generic;
using System.Xml;
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Transformation;

public class MoveTransform : TransformPrototype
{
    private int _frames;

    private int _loop;

    private int _loopFromInterval;

    private int _currentInterval;

    private List<MoveInterval> _intervals = new List<MoveInterval>();

    public override int Frames => _frames;

    public MoveTransform()
    {
        _Type = Type.Move;
    }

    public static MoveTransform Create(XmlNode node)
    {
        if (node == null) return null;
        var move = new MoveTransform();
        move.Parse(node);
        return move;
    }

    public override void Parse(XmlNode node)
    {
        _loop = node.Attributes["Cycle"].ParseInt();
        _loopFromInterval = node.Attributes["CycleFromInterval"].ParseInt();
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.Name == "MoveInterval")
            {
                var interval = MoveInterval.Create(child);
                _frames += interval.Frames;
                _intervals.Add(interval);
            }
        }
    }

    public override bool Iteration()
    {

        if (_Runner == null || _Pause)
        {
            return false;
        }

        if (_intervals.Count <= _currentInterval)
        {
            if (_loop == 0)
            {
                if (_loop != -1)
                {
                    _Runner.TweenPosition = _Runner.TweenPosition.Reset();
                    _Pause = true;
                    return false;
                }
            }
            else
            {
                _loop--;
            }
            _currentInterval = _loopFromInterval;
        }

        if (!_intervals[_currentInterval].Iteration(_Runner))
        {
            _currentInterval++;
        }
        return true;
    }


    public override void Reset()
    {
        _Pause = false;
        _currentInterval = 0;
        foreach (var interval in _intervals)
        {
            interval.Reset();
        }
    }

    public override TransformPrototype Clone()
    {
        var move = new MoveTransform();
        move._loop = _loop;
        _loopFromInterval = 0;
        move._loopFromInterval = _loopFromInterval;
        foreach (var i in _intervals)
        {
            move._intervals.Add(i.Clone());
        }
        return move;
    }

    public override void CalcDelta()
    {
    }
}
