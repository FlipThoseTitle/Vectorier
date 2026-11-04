// Ported from Vector-Raylib-For-Vectorier (the game's player movement), see Gameplay/README.md.
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Node;

namespace Vectorier.Gameplay.Controllers
{
    public class ControllerPhysics
    {
        private int _Iterative = 1;

        private ModelObject _ModelObject;

        private bool _IsPhysics;

        private float _SlowMode = 1;

        private bool[] SArray;

        private bool[] EArray;

        private List<ModelNode> _Nodes;

        private List<ModelLine> _Edges;

        private List<ModelLine> _EdgesPhysic;

        private static float _Gravity = 0.4000000059604645f;

        public ModelObject ModelObject => _ModelObject;

        public bool IsPhysics => _IsPhysics;

        public float SlowMode => _SlowMode;

        public static float Gravity => _Gravity / LevelMainController.current.slowModeFrames;

        public ControllerPhysics(ModelObject Object)
        {
            _ModelObject = Object;
            _Nodes = Object.NodesAll;
            _Edges = Object.EdgesAll;
            _EdgesPhysic = Object.EdgesPhysic;
        }

        public void Render()
        {
            TimeStep();
            Iterative();
            NodeReset();
        }

        public void Start()
        {
            _IsPhysics = true;
        }

        public void Stop()
        {
            _IsPhysics = false;
        }

        public void TimeStep()
        {
            ModelNode modelNode = null;
            for (int i = 0; i < _Nodes.Count; i++)
            {
                modelNode = _Nodes[i];
                if (!modelNode.IsFixed && (_IsPhysics || modelNode.IsPhysics))
                {
                    modelNode.TimeStep(Gravity);
                }
            }
        }

        public void Iterative()
        {
            if (!_IsPhysics)
            {
                foreach (var edge in _EdgesPhysic)
                {
                    IterativeLine(edge, _IsPhysics);
                }
            }
            else
            {
                foreach (var edge in _Edges)
                {
                    IterativeLine(edge, _IsPhysics);
                }
            }
        }

        public void IterativeLine(ModelLine line, bool isPhysics)
        {
            if (isPhysics || line.Start.IsPhysics || line.End.IsPhysics)
            {
                Vector3 p_vector = line.Iterative(line.Start.Start);
                Vector3 p_vector2 = line.Iterative(line.End.Start);
                IterativeNode(p_vector, line.Start, isPhysics);
                IterativeNode(p_vector2, line.End, isPhysics);
            }

        }

        public void IterativeNode(Vector3 vector, ModelNode node, bool isPhysics)
        {
            if (!node.IsType || node.IsFixed || (!isPhysics && !node.IsPhysics))
            {
                return;
            }
            node.PositionStart(vector);
        }

        public void NodeReset()
        {
            if (!_IsPhysics)
            {
                return;
            }
            _ModelObject.PositionToPivot(_ModelObject.DetectorHorizontalNode);
            _ModelObject.PositionToPivot(_ModelObject.DetectorVerticalNode);
            _ModelObject.PositionToPivot(_ModelObject.CameraNode.Node);
            _ModelObject.PositionToPivot(_ModelObject.CenterOfMassNode);
        }
    }
}
