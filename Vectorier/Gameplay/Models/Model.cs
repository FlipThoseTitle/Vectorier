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
using Vectorier.Gameplay.Controllers;
using Vectorier.Gameplay.Level;
using Vectorier.Gameplay.Node;
using Vectorier.Gameplay.Result;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Models
{
    public class Model : IDrawable
    {
        private bool _IsEnabled;

        private string _Name;

        protected ModelObject _ModelObject;

        protected ControllerPhysics _ControllerPhysics;

        protected ControllerCollisions _ControllerCollisions;

        protected ControllerStrike _controllerStrike;

        protected double _DeltaBox = 300;

        protected ModelType _Type;

        public const double BoundingBoxSize = 300.0;


        public virtual bool IsEnabled
        {
            get => _IsEnabled;
            set => _IsEnabled = value;
        }

        public bool IsVisible
        {
            get => _ModelObject.IsVisible;
            set => _ModelObject.IsVisible = value;
        }

        public string Name
        {
            get => _Name;
            set
            {
                _Name = value;
                _ModelObject.Name = value;
            }
        }

        public ModelObject ModelObject => _ModelObject;

        public ControllerPhysics ControllerPhysics => _ControllerPhysics;

        public ControllerCollisions ControllerCollisions => _ControllerCollisions;

        public ControllerStrike ControllerStrike => _controllerStrike;

        public double DeltaBox => _DeltaBox;

        public float Z { get; set; } = -100;

        public ModelType Type
        {
            get => _Type;
            set => _Type = value;
        }

        public Color Color
        {
            get => _ModelObject.Color;
            set => _ModelObject.Color = value;
        }

        public virtual Rectangle Rectangle => _ModelObject.Rectangle == null ? new Rectangle() : _ModelObject.Rectangle;

        public bool IsPhysics => _ControllerPhysics.IsPhysics;

        public bool DebugMode
        {
            set => _ModelObject.DebugMode = value;
        }

        public static List<Model> ModelsAll = new List<Model>();

        public Model(List<string> skins, ModelType type)
        {
            _Type = type;
            _ModelObject = new ModelObject(skins);
            _ModelObject.IsAuxiliary = false;
            _ModelObject.Parent = this;

            ModelsAll.Add(this);

            //GlobalObjects.AddDrawable(this);

        }

        public virtual void Draw()
        {
            if (VectorGame.RenderEdges)
            {
                for (int i = 0; i < _ModelObject.EdgesAll.Count; i++)
                {
                    _ModelObject.EdgesAll[i].Draw();
                }
            }
            if (VectorGame.RenderDetectors)
            {
                if (_ModelObject.DetectorHorizontalLine != null)
                    _ModelObject.DetectorHorizontalLine.Draw();

                if (_ModelObject.DetectorVerticalLine != null)
                    _ModelObject.DetectorVerticalLine.Draw();
            }
            if (VectorGame.NodeMode != VectorGame.NodeDisplay.None)
            {
                var nodes = _ModelObject.NodesAll;
                // Ordinary nodes first (All mode only), then COM / detectors / Camera on top so they stay visible.
                if (VectorGame.NodeMode == VectorGame.NodeDisplay.All)
                {
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        if (!nodes[i].IsImportant)
                            nodes[i].Draw();
                    }
                }
                for (int i = 0; i < nodes.Count; i++)
                {
                    if (nodes[i].GetImportantStyle(out float radius, out Color color))
                        nodes[i].Draw(radius, color);
                }
            }
            if (VectorGame.RenderBoxes)
            {
                var vs = Sets.Current.Containers[1];
                Raylib.DrawRectangleLinesEx(new Vectorier.Gameplay.Rl.Rectangle(vs.TransformPoint(Rectangle.Origin), Rectangle.Size * vs.FrameScale), 2, Color.Red);
            }

        }

        public virtual void Init()
        {
            _ControllerPhysics = new ControllerPhysics(_ModelObject);
            _ControllerCollisions = new ControllerCollisions(this);
            _controllerStrike = new ControllerStrike(this);
            _ControllerPhysics.Stop();
        }

        public virtual void StartPhysics()
        {
            _ControllerPhysics.Start();
        }

        public void StopPhysics()
        {
            _ControllerPhysics.Stop();
        }

        public virtual void Strike(ModelLine edge, Vector3 point, Vector3 impulse)
        {
            _controllerStrike.Striking(edge, point, impulse);
        }

        public virtual void OnCollisionPlatform(ModelEvent<Collision> Event)
        {
        }

        public virtual void OnCollisionModel(Collision collision)
        {
        }

        public virtual void Render(List<QuadRunner> platforms = null)
        {
        }

        public virtual void Reset()
        {
            _ModelObject.Reset();
        }

        public void Position(Vector3 vector, string Name = "NPivot")
        {
            _ModelObject.Position(vector, Name);
        }

        public Vector3 Position(string name = "NPivot", bool isCurrent = true)
        {
            return _ModelObject != null ? _ModelObject.Position(name, isCurrent) : default;
        }

        public ModelNode GetNode(string nodeName = "NPivot")
        {
            return _ModelObject.GetNode(nodeName);
        }

        public Vector2 GetPositionForIcon()
        {
            return _ModelObject.GetPositionForIcon();
        }

    }
}
