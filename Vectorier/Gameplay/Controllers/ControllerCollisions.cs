#nullable disable warnings
using System.Collections.Generic;
using System.Numerics;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Node;
using Vectorier.Gameplay.Result;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Controllers;

public class ControllerCollisions
{
    private ModelType _type;

    private Model _model;

    private ModelObject _modelObject;

    private List<ModelNode> _collisionNodes;

    private List<ModelLine> _collisionEdges;
    private ControllerAreas _controllerAreas;

    private ControllerCatching _modelControllerCatching;

    private List<Cross> cachedCrossList = new List<Cross>();

    public ControllerPlatforms ControllerPlatforms { get; }

    public List<AreaRunner> ActiveAreas => _controllerAreas.ActiveAreas;

    public bool IsPlay => _model.Type == ModelType.Human && ((ModelHuman)_model).IsPlay;

    public ControllerCollisions(Model model)
    {
        _model = model;
        _modelObject = model.ModelObject;
        _collisionEdges = _modelObject.CollisibleEdges;
        _collisionNodes = _modelObject.CollisibleNodes;
        _type = model.Type;
        if (model.Type == ModelType.Human)
        {
            ModelHuman modelHuman = (ModelHuman)model;
            ControllerPlatforms = new ControllerPlatforms(modelHuman);
            _controllerAreas = new ControllerAreas(modelHuman);
            _modelControllerCatching = new ControllerCatching(modelHuman);
        }
    }

    public void Reset()
    {
        if (_type != ModelType.Human)
        {
            return;
        }
        ControllerPlatforms.Reset();
        _controllerAreas.Reset();
        _modelControllerCatching.Reset();
    }

    public void UpdateQuad(QuadRunner quad, int sign)
    {
        if (quad is TrickAreaRunner trick && IsCollide(trick))
        {
            trick.ExpandTrickAreaOnce(sign);
        }
        UpdateQuad(quad);
    }

    public void UpdateQuad(QuadRunner quad)
    {
        quad.IsRender = false;
        if (!IsCollide(quad.rectangle) || !quad.IsEnabled)
        {
            return;
        }
        ModelHuman modelHuman = (ModelHuman)_model;
        switch (quad.TypeClass)
        {
            case RunnerType.Trigger:
                modelHuman.ControllerTrigger.Check((TriggerRunner)quad);
                return;
            case RunnerType.Area:
                _controllerAreas.Check((AreaRunner)quad);
                return;
        }
        if (modelHuman.IsPlay)
        {
            ControllerPlatforms.Render(quad);
            DispatchCollision(quad);
        }
        else
        {
            PushingNodes(quad);
        }
    }

    public void UpdatePlatform(QuadRunner platform)
    {
        if (IsCollide(platform.rectangle))
        {
            if (IsPlay)
            {
                ControllerPlatforms.Render(platform);
                DispatchCollision(platform);
            }
            else
            {
                PushingNodes(platform);
            }
        }
    }

    public void UpdatePrimitive(PrimitiveRunner primitive)
    {
        if (IsCollide(primitive.Rectangle))
        {
            CheckPrimitive(primitive);
        }
    }

    public void CheckModel(ModelHuman modelHuman)
    {
        _modelControllerCatching.Render(modelHuman);
    }

    public void CheckPrimitive(PrimitiveRunner primitive)
    {
        if (primitive.Model.Type == ModelType.Primitive)
        {
            foreach (var edge in _collisionEdges)
            {
                var collision = CrossModel(primitive.Model, edge.Start.Start, edge.End.Start);
                if (collision != null)
                {
                    var node = _modelObject.GetNode();
                    var vector = node.Start - node.End;
                    primitive.Model.Strike(collision.Edge, collision.Point, vector);
                    collision.Edge = edge;
                    collision.Model = primitive.Model;
                    _model.OnCollisionModel(collision);
                    return;
                }
            }
        }
    }

    public Collision CrossModel(Model model, Vector3 v1, Vector3 v2)
    {
        if (model == null || !model.IsEnabled)
        {
            return null;
        }
        foreach (ModelLine item in model.ModelObject.EdgesAll)
        {
            Vector3? vector3f = VectorUtils.Cross(v1, v2, item.Start.Start, item.End.Start);
            if (!vector3f.HasValue)
            {
                continue;
            }
            Collision collision = new Collision();
            collision.Point = vector3f.Value;
            collision.Edge = item;
            return collision;
        }
        return null;
    }

    public void DispatchCollision(QuadRunner platform)
    {
        ModelHuman modelHuman = _model as ModelHuman;
        if (modelHuman == null || !IsCollide(platform))
        {
            return;
        }
        ModelLine modelLine = null;
        for (int i = 0; i < _collisionEdges.Count; i++)
        {
            cachedCrossList.Clear();
            modelLine = _collisionEdges[i];
            platform.CrossByEdge(modelLine.Start.Start, modelLine.End.Start, cachedCrossList);
            if (cachedCrossList.Count != 0)
            {
                Collision collision = new Collision();
                collision.Model = _model;
                collision.Edge = modelLine;
                collision.Point = cachedCrossList[0].Point;
                collision.Platform = platform;
                Collision p_collision = collision;
                modelHuman.OnCollisionPlatform(new ModelEvent<Collision>(p_collision));
                break;
            }
        }
    }

    public void PushingNodes(QuadRunner platform)
    {
        foreach (ModelNode collisionNode in _collisionNodes)
        {
            cachedCrossList.Clear();
            Vector3Line vector3fLine = platform.Friction(collisionNode.End, collisionNode.Start, cachedCrossList);
            if (vector3fLine != null)
            {
                vector3fLine.Start.Z = collisionNode.Start.Z;
                vector3fLine.End.Z = collisionNode.End.Z;
                collisionNode.PositionStart(vector3fLine.Start);
                collisionNode.PositionEnd(vector3fLine.End);
            }
        }
    }

    // Reused by PushingNode (Friction clears it): pushing runs every tick for every node of every effect.
    private static readonly List<Cross> PushCrosses = new();

    public static void PushingNode(ModelNode collisionNode, List<QuadRunner> platforms, float restitution)
    {
        foreach (var platform in platforms)
        {
            var crossList = PushCrosses;

            Vector3 velocity = collisionNode.Start - collisionNode.End;

            Vector3Line pushed = platform.Friction(collisionNode.End, collisionNode.Start, crossList);
            if (pushed == null)
            {
                continue;
            }

            pushed.Start.Z = collisionNode.Start.Z;
            pushed.End.Z = collisionNode.End.Z;

            collisionNode.PositionStart(pushed.Start);
            collisionNode.PositionEnd(pushed.End);

            int side = crossList.Count > 0
                ? crossList[0].Index
                : platform.NearestEdge(collisionNode.Start);

            switch (side)
            {
                case 0:
                case 2:
                    velocity.Y = -velocity.Y * restitution;
                    break;

                case 1:
                case 3:
                    velocity.X = -velocity.X * restitution;
                    break;
            }

            collisionNode.PositionEnd(collisionNode.Start - velocity);

            break;
        }
    }

    public bool IsCollide(QuadRunner quad)
    {
        return ((ModelHuman)_model).CollisionBox.Intersect(quad.rectangle);
    }

    private bool IsCollide(Rectangle rectangle)
    {
        return rectangle.Intersect(_model.Rectangle);
    }
}
