// Ported from Vector-Raylib-For-Vectorier (the game's player movement), see Gameplay/README.md.
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vectorier.Gameplay.Geometry;
using Vectorier.Gameplay.Location;
using Vectorier.Gameplay.Node;

namespace Vectorier.Gameplay.Models
{
    public class ModelRender : IDrawable
    {
        private Color _Color;

        private bool _debugMode;

        private bool _IsEnabled = true;

        private List<Capsule> _Capsules = new List<Capsule>();

        private List<Triangle> _Triangles = new List<Triangle>();

        public float Z { get; set; } = -15;

        public Color Color
        {
            get => _Color;
            set => _Color = value;
        }

        public string Name
        {
            get;
            set;
        }

        public bool DebugMode
        {
            get => _debugMode;
            set => _debugMode = value;
        }

        public bool IsEnabled
        {
            get => _IsEnabled;
            set
            {
                _IsEnabled = value;
            }
        }

        public List<Capsule> Capsules => _Capsules;

        public List<Triangle> Triangles => _Triangles;

        public ModelRender()
        {
        }

        public void Draw()
        {
            foreach (var capsule in _Capsules)
            {
                capsule.Draw(Color);
            }
            foreach (var triangle in _Triangles)
            {
                triangle.Draw(Color);
            }
        }

        public void Add(List<ModelNode> Nodes)
        {
        }

        public void Add(ModelNode Node)
        {
        }

        public void Add(List<ModelLine> Lines)
        {
        }

        public void Add(ModelLine modelLine)
        {
            switch (modelLine.Type)
            {
                case "Capsule":
                    var capsule = new Capsule();
                    capsule.Init(modelLine);
                    _Capsules.Add(capsule);
                    break;
            }
        }

        public void Add(List<Triangle> Triangles)
        {
            _Triangles.AddRange(Triangles);
        }

        public void Add(Triangle Triangles)
        {
            _Triangles.Add(Triangles);
        }

        public void Add(Model Model)
        {
        }

        public Capsule GetCapsulTransform(string name)
        {
            foreach (var c in _Capsules)
            {
                if (c.Name == name)
                {
                    return c;
                }
            }
            return null;
        }
    }
}
