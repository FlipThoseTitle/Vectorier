#nullable disable warnings
using System.Collections.Generic;
using System.Numerics;
using System.Xml;
using Vectorier.Gameplay.Runners;
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Transformation;

public class SizeTransform : TransformPrototype
{
    private int _frames;

    private int _currentFrame;

    private float _finalWidth;

    private float _finalHeight;

    private List<Vector2> _points = new List<Vector2>();

    public override int Frames => _frames;

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
        _frames = node.Attributes["Frames"].ParseInt();

        _finalWidth = node.Attributes["FinalWidth"].ParseFloat(-1);
        _finalHeight = node.Attributes["FinalHeight"].ParseFloat(-1);
    }

    protected void ReplaceZeroByEps(Vector2 point)
    {
        if (point.X == 0f)
        {
            point.X = 1E-05f;
        }
        if (point.Y == 0f)
        {
            point.Y = 1E-05f;
        }
    }

    public override bool Iteration()
    {
        if (_Runner == null || _Pause)
        {
            return false;
        }
        _Runner.TransformResize(_points[_currentFrame]);
        _currentFrame++;
        if (_currentFrame >= _frames)
        {
            _Pause = true;
            return false;
        }
        return true;
    }

    public override void CalcDelta()
    {
        _points.Clear();
        var visualRunner = _Runner as VisualRunner;
        var quadRunner = _Runner as QuadRunner;
        if (visualRunner != null)
        {
            if (_finalWidth < 0)
            {
                _finalWidth = visualRunner.Width;
            }
            if (_finalHeight < 0)
            {
                _finalHeight = visualRunner.Height;
            }
            _points.Add(new Vector2(visualRunner.Width, visualRunner.Height));
        }
        if (quadRunner != null)
        {
            if (_finalWidth < 0)
            {
                _finalWidth = quadRunner.Width;
            }
            if (_finalHeight < 0)
            {
                _finalHeight = quadRunner.Height;
            }
            _points.Add(new Vector2(quadRunner.Width, quadRunner.Height));
        }
        _points.Add(new Vector2(_finalWidth, _finalHeight));
        CalculatePoints();
    }

    private void CalculatePoints()
    {
        Vector2 point = _points[0];
        Vector2 point2 = _points[_points.Count - 1];
        List<Vector2> list = new List<Vector2>();
        for (int i = 1; i < _points.Count - 1; i++)
        {
            list.Add(_points[i]);
        }
        _points.Clear();
        float num = 0f;
        for (int j = 0; j < _frames; j++)
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
            _points.Add(new Vector2((float)num3, (float)num4));
            num += 1f / _frames;
        }
        _points.Add(point2);
        ReplaceZeroByEps(_points[_points.Count - 1]);
        for (int num1 = _points.Count - 1; num > 1; num--)
        {
            ReplaceZeroByEps(_points[num1 - 1]);
        }
        _points.RemoveAt(0);
    }

    public override void Reset()
    {
        base.Reset();
        _currentFrame = 0;
    }

    public override TransformPrototype Clone()
    {
        var size = new SizeTransform();
        size._frames = _frames;
        size._finalHeight = _finalHeight;
        size._finalWidth = _finalWidth;
        return size;
    }
}

