#nullable disable warnings
using System.Collections.Generic;
using System.Numerics;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Node;

namespace Vectorier.Gameplay.Controllers;

public class ControllerPhysics
{
    private ModelObject _modelObject;
    private List<ModelNode> _nodes;

    private List<ModelLine> _edges;

    private List<ModelLine> _edgesPhysic;

    public bool IsPhysics { get; private set; }

    // Outside a level (the editor's model previews) there is no slow mode.
    public static float Gravity { get => field / (LevelMainController.current?.slowModeFrames ?? 1); } = 0.4000000059604645f;

    public ControllerPhysics(ModelObject Object)
    {
        _modelObject = Object;
        _nodes = Object.NodesAll;
        _edges = Object.EdgesAll;
        _edgesPhysic = Object.EdgesPhysic;
    }

    public void Render()
    {
        TimeStep();
        Iterative();
        NodeReset();
    }

    public void Start()
    {
        IsPhysics = true;
    }

    public void Stop()
    {
        IsPhysics = false;
    }

    public void TimeStep()
    {
        ModelNode modelNode = null;
        for (int i = 0; i < _nodes.Count; i++)
        {
            modelNode = _nodes[i];
            if (!modelNode.IsFixed && (IsPhysics || modelNode.IsPhysics))
            {
                modelNode.TimeStep(Gravity);
            }
        }
    }

    public void Iterative()
    {
        if (!IsPhysics)
        {
            foreach (var edge in _edgesPhysic)
            {
                IterativeLine(edge, IsPhysics);
            }
        }
        else
        {
            foreach (var edge in _edges)
            {
                IterativeLine(edge, IsPhysics);
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
        if (!IsPhysics)
        {
            return;
        }
        _modelObject.PositionToPivot(_modelObject.DetectorHorizontalNode);
        _modelObject.PositionToPivot(_modelObject.DetectorVerticalNode);
        _modelObject.PositionToPivot(_modelObject.CameraNode.Node);
        _modelObject.PositionToPivot(_modelObject.CenterOfMassNode);
    }
}
