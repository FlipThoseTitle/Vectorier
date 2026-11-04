// Ported from Vector-Raylib-For-Vectorier (the game's player movement), see Gameplay/README.md.
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Vectorier.Gameplay.Location;

namespace Vectorier.Gameplay.Node
{
    public class ModelLine : IDrawable
    {
        private string _name;

        public ModelNode Start
        {
            get;
        }

        public ModelNode End
        {
            get;
        }

        public Vector3Line LineCurrent
        {
            get;
        }

        public Vector3Line LinePrevious
        {
            get;
        }

        public virtual string Name
        {
            get => _name;
            set => _name = value;
        }

        public Color Color
        {
            get;
            set;
        }

        public float Length
        {
            get;
            set;
        }

        private float RealLength => Vector3.Distance(Start.Start, End.Start);

        private float ScaleLength => Length / RealLength;

        public int SubType
        {
            get;
            set;
        }

        public string Type
        {
            get;
            set;
        }

        public float Mass
        {
            get;
        }

        public float Margin1
        {
            get;
            set;
        }

        public float Margin2
        {
            get;
            set;
        }

        public bool Collisible
        {
            get;
            set;
        }

        public float Stroke
        {
            get;
            set;
        }

        public Vector3 Center => VectorUtils.Middle(Start.Start, End.Start);

        public float Z { get; set; } = -100;

        public ModelLine(ModelNode node1, ModelNode node2)
        {
            LineCurrent = new Vector3Line(node1.Start, node2.Start);
            LinePrevious = new Vector3Line(node1.End, node2.End);
            Start = node1;
            End = node2;
            Mass = 1f / (Start.Weight + End.Weight);

            //GlobalObjects.Drawables.Add(this);
        }

        public void Draw()
        {
            // Drawing is done by the editor (Controls/Scene/SceneView.Gameplay.cs).
        }

        public ModelLine(ModelLine modelLine)
        {
            Name = modelLine.Name;
            Start = modelLine.Start;
            End = modelLine.End;
            Stroke = modelLine.Stroke;
            Color = modelLine.Color;
            Length = modelLine.Length;
            SubType = modelLine.SubType;
            Type = modelLine.Type;
            Margin1 = modelLine.Margin1;
            Margin2 = modelLine.Margin2;
        }

        public Vector3 Iterative(Vector3 vector)
        {
            var v = Start.Start.Multiply(Start.Weight) + End.Start.Multiply(End.Weight);
            Vector3 vector3f = v.Multiply(Mass);
            var v2 = (vector - vector3f);
            Vector3 vector3f2 = v2.Multiply(ScaleLength);
            return vector3f + vector3f2;

        }
    }
}
