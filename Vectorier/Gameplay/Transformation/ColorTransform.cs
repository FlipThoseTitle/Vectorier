// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vectorier.Gameplay.Rl;
using System.Xml;
using Vectorier.Gameplay.Runners;
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Transformation
{
    public class ColorTransform : TransformPrototype
    {
        private int _Frames;

        private int _CurrentFrame;

        private Color _StartColor;

        private Color _EndColor;

        private Color _DeltaColor;

        public override int Frames => _Frames;

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
            _Frames = (int)node.Attributes["Frames"].ParseFloat(1f);
            ParseColor(node.Attributes["ColorStart"], ref _StartColor, new Color(-1f, -1f, -1f, -1f));
            ParseColor(node.Attributes["ColorFinish"], ref _EndColor, new Color(1f, 1f, 1f, 1f));
        }

        private static void ParseColor(XmlAttribute p_attr, ref Color p_color, Color p_defColor)
        {
            if (p_attr == null || p_attr.Value.Length == 0)
            {
                p_color = p_defColor;
            }
            else
            {
                p_color = ColorUtils.FromHex(p_attr.Value);
            }
        }

        public override bool Iteration()
        {
            if (_Runner == null || _Pause)
            {
                return false;
            }
            _CurrentFrame++;
            if (_CurrentFrame >= _Frames)
            {
                _Runner.TransformColorEnd(_EndColor);
                _Pause = true;
                return false;
            }

            _Runner.TransformColor(_DeltaColor);
            return true;
        }

        public override void CalcDelta()
        {
            VisualRunner visualRunner = _Runner as VisualRunner;
            if (_StartColor.R < 0f)
            {
                if (visualRunner != null)
                {
                    _StartColor = visualRunner.Color;
                }
            }
            else
            {
                if (visualRunner != null)
                {
                    visualRunner.Color = _StartColor;
                }
            }
            _DeltaColor = ColorUtils.Substract(_EndColor, _StartColor);
            _DeltaColor = _DeltaColor.Divide(_Frames);
        }

        public override void Reset()
        {
            base.Reset();
            _CurrentFrame = 0;
        }

        public override TransformPrototype Clone()
        {
            var color = new ColorTransform();
            color._StartColor = _StartColor;
            color._EndColor = _EndColor;
            color._DeltaColor = _DeltaColor;
            color._Frames = _Frames;
            return color;
        }
    }
}
