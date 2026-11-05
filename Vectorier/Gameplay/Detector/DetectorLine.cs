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
using Vectorier.Gameplay.Node;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Detector
{
    public class DetectorLine : IDrawable
    {
        public enum DetectorType
        {
            Vertical,
            Horizontal
        }

        private Vector3 lineDirection = new Vector3();

        public ModelNode Node
        {
            get;
        }

        public QuadRunner Platform
        {
            get => Node.Data;
            set => Node.Data = value;
        }

        public string Name => Node.Name;

        public Vector3 Position => Node.Start;

        public DetectorType Type
        {
            get;
        }

        public Vector3 DeltaValue;

        public bool Safe
        {
            get;
            set;
        }

        public int Side
        {
            get;
            set;
        }

        public Vector3Line End
        {
            get;
        }

        public Vector3Line Start
        {
            get;
        }

        public Vector3Line Perpendicular
        {
            get;
        }

        public float Z { get; set; } = -100;

        public DetectorLine(ModelNode node, DetectorType type)
        {
            Node = node;
            Start = new Vector3Line(node.Start, node.Start);
            End = new Vector3Line(node.Start, node.Start);
            Perpendicular = new Vector3Line(node.Start, node.Start);
            Type = type;
            DeltaValue = new Vector3();

            //GlobalObjects.AddDrawable(this);
        }

        public void Reset()
        {
            Start.Set(Node.Start, Node.Start);
            End.Set(Node.Start, Node.Start);
            Perpendicular.Set(Node.Start, Node.Start);
        }

        public Vector3 Subtract()
        {
            return Node.Start - Node.End;
        }

        public void Delta(float value)
        {
            switch (Type)
            {
                case DetectorType.Horizontal:
                    DeltaValue.X = value;
                    break;
                case DetectorType.Vertical:
                    DeltaValue.Y = value;
                    break;
            }
        }

        public void Update()
        {
            End.Set(Start.Start, Start.End);
            Start.Set(Node.Start, Node.Start);
            Start.SetZerroOnZ();
            Start.Start = Start.Start.Subtract(DeltaValue);
            Start.End = Start.End.Add(DeltaValue);
            lineDirection = lineDirection.Set(End.End);
            lineDirection = lineDirection.Subtract(End.Start);
            Perpendicular.Start = VectorUtils.Closest(Start.Start, End.Start, lineDirection);
            lineDirection = lineDirection.Set(End.Start);
            lineDirection = lineDirection.Subtract(End.End);
            Perpendicular.End = VectorUtils.Closest(Start.End, End.End, lineDirection);

            //Start.Start = Start.Start.Normalize();
            //Start.End = Start.End.Normalize();
        }

        public void DeltaPosition(Vector3 delta)
        {
            Node.Start = Node.Start.Add(delta);
            Node.EndAssignStart();
        }

        public void Draw()
        {
            var vs = Sets.Current.Containers[1];
            var start = vs.TransformPoint(Start.Start);
            Raylib.DrawLineEx(start, vs.TransformPoint(Start.End), 3, Color.Yellow);

            GameFont.DrawUi(Start.Start.ToString(), start.X, start.Y, 5, Color.White);
        }

        public string GetStringType()
        {
            return Type != 0 ? "H" : "V";
        }
    }
}
