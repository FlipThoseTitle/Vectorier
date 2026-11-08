#nullable disable warnings
using System.Xml;
using Vectorier.Gameplay.Rl;
using Vectorier.Gameplay.Runners;
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Transformation;

public class ColorTransform : TransformPrototype
{
    private int _frames;

    private int _currentFrame;

    private Color _startColor;

    private Color _endColor;

    private Color _deltaColor;

    public override int Frames => _frames;

    public ColorTransform()
    {
        _Type = Type.Color;
    }

    public static ColorTransform Create(XmlNode node)
    {
        if (node == null) return null;
        var color = new ColorTransform();
        color.Parse(node);
        return color;
    }

    public override void Parse(XmlNode node)
    {
        _frames = (int)node.Attributes["Frames"].ParseFloat(1f);
        ParseColor(node.Attributes["ColorStart"], ref _startColor, new Color(-1f, -1f, -1f, -1f));
        ParseColor(node.Attributes["ColorFinish"], ref _endColor, new Color(1f, 1f, 1f, 1f));
    }

    private static void ParseColor(XmlAttribute attr, ref Color color, Color defColor)
    {
        if (attr == null || attr.Value.Length == 0)
        {
            color = defColor;
        }
        else
        {
            color = ColorUtils.FromHex(attr.Value);
        }
    }

    public override bool Iteration()
    {
        if (_Runner == null || _Pause)
        {
            return false;
        }
        _currentFrame++;
        if (_currentFrame >= _frames)
        {
            _Runner.TransformColorEnd(_endColor);
            _Pause = true;
            return false;
        }

        _Runner.TransformColor(_deltaColor);
        return true;
    }

    public override void CalcDelta()
    {
        VisualRunner visualRunner = _Runner as VisualRunner;
        if (_startColor.R < 0f)
        {
            if (visualRunner != null)
            {
                _startColor = visualRunner.Color;
            }
        }
        else
        {
            if (visualRunner != null)
            {
                visualRunner.Color = _startColor;
            }
        }
        _deltaColor = ColorUtils.Substract(_endColor, _startColor);
        _deltaColor = _deltaColor.Divide(_frames);
    }

    public override void Reset()
    {
        base.Reset();
        _currentFrame = 0;
    }

    public override TransformPrototype Clone()
    {
        var color = new ColorTransform();
        color._startColor = _startColor;
        color._endColor = _endColor;
        color._deltaColor = _deltaColor;
        color._frames = _frames;
        return color;
    }
}
