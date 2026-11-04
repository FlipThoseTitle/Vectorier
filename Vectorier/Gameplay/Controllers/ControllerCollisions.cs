// Ported from Vector-Raylib-For-Vectorier (the game's player movement and model drawing).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Node;
using Vectorier.Gameplay.Result;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Controllers
{
    public class ControllerCollisions
    {
        private ModelType _Type;

        private Model _Model;

        private ModelObject _ModelObject;

        private List<ModelNode> _CollisionNodes;

        private List<ModelLine> _CollisionEdges;

        private ControllerPlatforms _controllerPlatforms;

        private ControllerAreas _controllerAreas;

        private ControllerCatching _modelControllerCatching;

        private List<Cross> cachedCrossList = new List<Cross>();

        public ControllerPlatforms ControllerPlatforms => _controllerPlatforms;

        public ControllerCatching ModelControllerCatching => _modelControllerCatching;

        public List<AreaRunner> ActiveAreas => _controllerAreas.ActiveAreas;

        public bool IsPlay => _Model.Type == ModelType.Human && ((ModelHuman)_Model).IsPlay;

        public ControllerCollisions(Model model)
        {
            _Model = model;
            _ModelObject = model.ModelObject;
            _CollisionEdges = _ModelObject.CollisibleEdges;
            _CollisionNodes = _ModelObject.CollisibleNodes;
            _Type = model.Type;
            if (model.Type == ModelType.Human)
            {
                ModelHuman modelHuman = (ModelHuman)model;
                _controllerPlatforms = new ControllerPlatforms(modelHuman);
                _controllerAreas = new ControllerAreas(modelHuman);
                _modelControllerCatching = new ControllerCatching(modelHuman);
            }
        }

        public void Reset()
        {
            if (_Type != ModelType.Human)
            {
                return;
            }
            _controllerPlatforms.Reset();
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
            ModelHuman modelHuman = (ModelHuman)_Model;
            switch (quad.TypeClass)
            {
                case RunnerType.Area:
                    _controllerAreas.Check((AreaRunner)quad);
                    return;
            }
            if (modelHuman.IsPlay)
            {
                _controllerPlatforms.Render(quad);
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
                    _controllerPlatforms.Render(platform);
                    DispatchCollision(platform);
                }
                else
                {
                    PushingNodes(platform);
                }
            }
        }


        public void CheckModel(ModelHuman modelHuman)
        {
            _modelControllerCatching.Render(modelHuman);
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
            ModelHuman modelHuman = _Model as ModelHuman;
            if (modelHuman == null || !IsCollide(platform))
            {
                return;
            }
            ModelLine modelLine = null;
            for (int i = 0; i < _CollisionEdges.Count; i++)
            {
                cachedCrossList.Clear();
                modelLine = _CollisionEdges[i];
                platform.CrossByEdge(modelLine.Start.Start, modelLine.End.Start, cachedCrossList);
                if (cachedCrossList.Count != 0)
                {
                    Collision collision = new Collision();
                    collision.Model = _Model;
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
            foreach (ModelNode collisionNode in _CollisionNodes)
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

        public static void PushingNode(ModelNode collisionNode, List<QuadRunner> platforms, float restitution)
        {
            foreach (var platform in platforms)
            {
                var crossList = new List<Cross>();

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
            return ((ModelHuman)_Model).CollisionBox.Intersect(quad.rectangle);
        }

        private bool IsCollide(Rectangle rectangle)
        {
            return rectangle.Intersect(_Model.Rectangle);
        }
    }
}
