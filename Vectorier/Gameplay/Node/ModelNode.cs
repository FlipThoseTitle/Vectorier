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
using System.Numerics;
using Vectorier.Gameplay.Level;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Node
{
    public class ModelNode : Vector3Line, IDrawable
    {
        private string _name;

        private int _bothIndex;

        private string _bothName;

        private readonly Vector3 _defaultPosition;

        private QuadRunner _Data;

        private Vector3 _timeStepVector = new Vector3();

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                string[] array = _name.Split('_');
                _bothName = _name.Split('_')[0];
                if (array.Length == 2 && int.TryParse(array[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int val))
                {
                    _bothIndex = val;
                }
                else
                {
                    _bothIndex = 0;
                }
            }
        }

        public int Id
        {
            get;
            set;
        }

        public bool IsFixed
        {
            get;
            set;
        }

        public bool IsNodeFixed => !IsFixed && IsType;

        public int Radius
        {
            get;
            set;
        }

        public bool IsPhysics
        {
            get;
            set;
        }

        public bool IsNodePhysics => IsPhysics && IsType;

        public float Weight
        {
            get;
            set;
        }

        public float Attenuation
        {
            get;
            set;
        }

        public string Type
        {
            get;
            set;
        }

        public bool IsType => Type == "Node";

        public MacroNode MacroNode
        {
            get;
            set;
        }

        public bool IsCollisible
        {
            get;
            set;
        }

        public bool IsDetector
        {
            get;
            set;
        }

        public int BothIndex => _bothIndex;

        public string BothName => _bothName;

        public QuadRunner Data
        {
            get => _Data;
            set => _Data = value;
        }

        public float Z { get; set; } = -100;

        public ModelNode(Vector3 position, MacroNode macroNode = null) : base(position, position)
        {
            _defaultPosition = position;
            MacroNode = macroNode;
            _Data = null;

            //GlobalObjects.AddDrawable(this);
        }

        /// <summary>Radius of an ordinary node's dot (world units).</summary>
        public const float NodeRadius = 1.2f;

        /// <summary>COM and Camera dots: the biggest.</summary>
        public const float CenterRadius = 2.4f;

        /// <summary>DetectorH / DetectorV dots: bigger than a node, smaller than COM.</summary>
        public const float DetectorRadius = 1.8f;

        public static readonly Color CenterColor = new Color(40, 120, 255, 255);
        public static readonly Color DetectorColor = new Color(235, 40, 40, 255);
        public static readonly Color CameraColor = new Color(255, 215, 0, 255);

        /// <summary>The nodes the N key's "Important" mode keeps: COM, DetectorH, DetectorV and Camera.</summary>
        public bool IsImportant => GetImportantStyle(out _, out _);

        /// <summary>Size and color of a special node (COM, detectors, Camera); false for an ordinary node.</summary>
        public bool GetImportantStyle(out float radius, out Color color)
        {
            switch (_name)
            {
                case "COM":
                    radius = CenterRadius;
                    color = CenterColor;
                    return true;
                case "DetectorH":
                case "DetectorV":
                    radius = DetectorRadius;
                    color = DetectorColor;
                    return true;
                case "Camera":
                    radius = CenterRadius;
                    color = CameraColor;
                    return true;
                default:
                    radius = NodeRadius;
                    color = Color.Green;
                    return false;
            }
        }

        public void Draw()
        {
            GetImportantStyle(out float radius, out Color color);
            Draw(radius, color);
        }

        public void Draw(float radius, Color color)
        {
            var vs = Sets.Current.Containers[1];
            Vector2 start = vs.TransformPoint(Start);
            Vector2 end = vs.TransformPoint(End);

            Raylib.DrawCircleV(start, radius, color);

            Vectorier.Gameplay.Rl.Raylib.DrawLineV(start, end, color);
        }

        public void Reset()
        {
            Start = Start.Set(_defaultPosition);
            End = End.Set(_defaultPosition);
            _Data = null;
        }

        public void Position(Vector3 end, Vector3 start)
        {
            End = End.Set(end);
            Start = Start.Set(start);
        }

        public void PositionStart(Vector3 vector)
        {
            Start = Start.Set(vector);
        }

        public void PositionStart(float x, float y, float z)
        {
            Start = Start.Set(x, y, z);
        }

        public void PositionStartNoRound(float x, float y, float z)
        {
            Start = Start.Set(x, y, z);
        }

        public void PositionEnd(Vector3 vector)
        {
            End = End.Set(vector);
        }

        public void PositionEnd(float x, float y, float z)
        {
            End = End.Set(x, y, z);
        }

        public void EndAssignStart()
        {
            End.X = Start.X;
            End.Y = Start.Y;
            End.Z = Start.Z;
        }

        public void MacroNodeCompute()
        {
            if (MacroNode != null)
            {
                Start = Start.Reset();
                End = End.Reset();
                for (int i = 0; i < MacroNode.ChildNode.Count; i++)
                {
                    Start = Start.Add(MacroNode.ChildNode[i].Start, MacroNode.LCC[i]);
                    End = End.Add(MacroNode.ChildNode[i].End, MacroNode.LCC[i]);
                }
            }
        }

        public void TimeStep(float gravity)
        {
            _timeStepVector = _timeStepVector.Set(Start.X - End.X, Start.Y - End.Y, Start.Z - End.Z);
            if (IsPhysics)
            {
                _timeStepVector = _timeStepVector.Multiply(1f - Attenuation);
            }
            _timeStepVector = _timeStepVector.Add(Start);
            _timeStepVector.Y += gravity;
            EndAssignStart();
            PositionStart(_timeStepVector);
        }
    }
}
