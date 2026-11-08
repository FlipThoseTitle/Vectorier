#nullable disable warnings
using System.Collections.Generic;
using System.Xml;
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Transformation;

public class RotateTransform : TransformPrototype
{
    private int _frames;

    private int _currentFrame;

    List<float> _angles = new List<float>();

    public override int Frames => _frames;

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
        _frames = node.Attributes["Frames"].ParseInt();
        float num = node.Attributes["Angle"].ParseFloat();
        CalcLinear(num);
    }

    private void CalcLinear(float angel)
    {
        float item = angel / _frames;
        for (int i = 0; i < _frames; i++)
        {
            _angles.Add(item);
        }
    }

    public override bool Iteration()
    {

        if (_Runner == null || _Pause)
        {
            return false;
        }
        _Runner.TransformRotate(_angles[_currentFrame]);
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
    }

    public override void Reset()
    {
        base.Reset();
        _currentFrame = 0;
    }

    public override TransformPrototype Clone()
    {
        var rotate = new RotateTransform();
        rotate._frames = _frames;
        foreach (var angle in _angles)
        {
            rotate._angles.Add(angle);
        }
        return rotate;
    }
}

