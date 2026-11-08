#nullable disable warnings
using System.Collections.Generic;
using System.Numerics;
using System.Xml;
using Vectorier.Gameplay.Runners;
using Vectorier.Gameplay.Utilities;
using Math = System.Math;

namespace Vectorier.Gameplay.Transformation;

public class MoveInterval
{
    private int _number;
    private float _delay;

    private float _currentDelay;

    private List<Vector2> _points = new List<Vector2>();

    private int _currentFrame;

    public int Frames { get; set; }

    public float Delay
    {
        get => _delay;
        set
        {
            _delay = value;
            _currentDelay = value;
        }
    }

    public MoveInterval()
    {
    }

    public MoveInterval(int number, int frames, float delay)
    {
        _number = number;
        Frames = frames;
        _currentFrame = 0;
        _delay = delay;
        _currentDelay = _delay;
    }

    public void Reset()
    {
        _currentFrame = 0;
        _currentDelay = _delay;
    }

    public void CalculateExtraPoints()
    {
        Vector2 vector3f = _points[0];
        Vector2 vector3f2 = _points[_points.Count - 1];
        List<Vector2> list = new List<Vector2>();
        for (int i = 1; i < _points.Count - 1; i++)
        {
            list.Add(_points[i]);
        }
        _points.Clear();
        float num = 0f;
        for (int j = 0; j < Frames; j++)
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
            _points.Add(new Vector2((int)num3, (int)num4));
            num += 1f / (float)Frames;
        }
        _points.Add(vector3f2);

        for (int num2 = _points.Count - 1; num2 > 0; num2--)
        {
            var v = _points[num2];
            v = v.Subtract(_points[num2 - 1]);
            v = v.Round(100);
            _points[num2] = v;
        }
        _points.RemoveAt(0);
    }


    public bool Iteration(Runner runner)
    {

        if (_currentDelay <= 0)
        {
            runner.UpdatePosition(_points[_currentFrame]);

            _currentFrame++;
            if (_currentFrame < _points.Count)
            {
                return true;
            }
            _currentFrame = 0;
            _currentDelay = _delay;
            return false;
        }
        runner.TweenPosition = runner.TweenPosition.Reset();
        _currentDelay--;
        return true;
    }

    public static MoveInterval Create(XmlNode node)
    {
        var moveInterval = new MoveInterval();
        moveInterval._number = node.Attributes["Number"].ParseInt();
        moveInterval.Frames = node.Attributes["FramesToMove"].ParseInt();
        var delay = node.Attributes["Delay"].ParseFloat();
        moveInterval._delay = delay;
        moveInterval._currentDelay = delay;
        foreach (XmlNode node2 in node.ChildNodes)
        {
            var point = VectorUtils.Create(node2);
            if (point.HasValue)
            {
                moveInterval._points.Add(point.Value);
            }
        }
        moveInterval.CalculateExtraPoints();
        return moveInterval;
    }

    public MoveInterval Clone()
    {
        var clone = new MoveInterval(_number, Frames, _delay);
        foreach (var point in _points)
        {
            var c = VectorUtils.Clone(point);
            clone._points.Add(c);
        }
        return clone;
    }
}
