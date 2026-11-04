// Ported from Vector-Raylib-For-Vectorier (the game's player movement and model drawing).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Vectorier.Gameplay.Location;
using Vectorier.Gameplay.Node;

namespace Vectorier.Gameplay.Camera
{
    public class LocationCamera
    {

        private float _Zoom = 1;

        private ModelNode _Position = new ModelNode(new Vector3());

        private bool _IsDrag;

        private CameraNode _Node;

        private float _TempZoom;

        private bool _IsRender;

        private bool _IgnoreZoom;

        private int _EffectFrames;

        private int _ActionLaunched;

        private static LocationCamera _Current;

        public static float MinZoom = 0.1f;

        public static float MaxZoom = 1.3f;

        public static float CurrentZoom = 0.5f;

        public static float FluencyCurrent = 2;

        public float Zoom => _Zoom;

        public CameraNode Node
        {
            get => _Node;
            set => _Node = value;
        }

        public bool IsRender => _IsRender;

        public bool IgnoreZoom
        {
            get => _IgnoreZoom;
            set => _IgnoreZoom = value;
        }

        public static LocationCamera Current => _Current;

        public float X => (float)_Position.Start.X;

        public float Y => (float)_Position.Start.Y;

        public Vector3 Position
        {
            get
            {
                return _Position.Start;
            }
            set
            {
                _Position.Start = value;
            }
        }

        public void Init()
        {
            _Position.Attenuation = 0;
            _Current = this;
            _IsRender = true;
        }

        public void Reset()
        {
            _Position.PositionStart(new Vector3(0, 0, 0));
            _Position.PositionEnd(new Vector3(0, 0, 0));
            _IsRender = true;
            _Node = null;
        }

        public void StartPosition(Vector3 p_position)
        {
            _Position = new ModelNode(p_position);
            _Node = new CameraNode(_Position);
        }

        public void UpdatePosition()
        {
            if (!_IsRender)
            {
                return;
            }
            _Position.TimeStep(0);
            var nodeStart = _Node.Start;
            var nodeEnd = _Node.End;
            var posStart = _Position.Start;
            var posEnd = _Position.End;

            nodeStart.Z = 0;
            nodeEnd.Z = 0;
            posStart.Z = 0;
            posEnd.Z = 0;

            Vector3 v1 = posEnd + nodeStart;
            nodeEnd = v1 - nodeEnd;
            nodeEnd = nodeEnd - posStart;

            nodeStart = nodeStart - posStart;
            nodeStart = nodeStart.Multiply(0.15);
            nodeEnd = nodeEnd + nodeStart;

            if (FluencyCurrent < nodeEnd.Length())
            {
                nodeEnd = nodeEnd.Normalize();
                nodeEnd = nodeEnd.Multiply(FluencyCurrent);
            }
            posStart.Add(nodeEnd);
            nodeEnd = posStart - posEnd;

            if (nodeEnd.Length() > 50)
            {
                nodeEnd = nodeEnd.Multiply(50 / nodeEnd.Length());
                posStart = posEnd + nodeEnd;
            }
            _Position.PositionStart(posStart);
        }

        public void Stop()
        {
            Vector3 p_position = _Node.Start + (_Node.Start - _Node.End) * 20f;
            _Node = new CameraNode(new ModelNode(p_position));
        }

        public void Zooming(float p_value = 1f, bool p_isStart = false)
        {
            if (_Zoom != p_value && !_IgnoreZoom)
            {
                if (p_value < MinZoom)
                {
                    p_value = MinZoom;
                }
                if (p_value > MaxZoom)
                {
                    p_value = MaxZoom;
                }
                // The game also zooms its parallax layers here; the editor's views keep their own zoom.
                _Zoom = p_value;
            }
        }

        public void ZoomIncrease(float p_value)
        {
            Zooming(_Zoom + p_value);
        }

        public void ZoomReduce(float p_value)
        {
            Zooming(_Zoom - p_value);
        }
    }
}
