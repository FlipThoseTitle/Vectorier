// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Numerics;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Node;

namespace Vectorier.Gameplay.Controllers
{
    public class ControllerStrike
    {
        private Model _Model;

        private ModelObject _ModelObject;

        private List<ModelNode> _Nodes;

        private List<ModelLine> _Edges;

        public ControllerStrike(Model model)
        {
            _Model = model;
            _ModelObject = _Model.ModelObject;
            _Nodes = _ModelObject.CollisibleNodes;
            _Edges = _ModelObject.CollisibleEdges;
        }

        public void Striking(ModelLine line, Vector3 point, Vector3 impulse)
        {
            Decrease();
            ModelNode start = line.Start;
            ModelNode end = line.End;
            if (!start.IsFixed || !end.IsFixed)
            {
                Vector3 start2 = start.Start;
                Vector3 start3 = end.Start;
                float num = VectorUtils.Factor(point, start2, start3);
                Vector3 Vector3 = new Vector3(1f - num, 1f - num, 0f);
                Vector3 Vector32 = new Vector3(num, num, 0f);
                float slowModeValue = LevelMainController.current.slowModeFrames;
                if (!start.IsFixed)
                {
                    float p_x = start2.X + Vector3.X * impulse.X / MathF.Sqrt(slowModeValue) / start.Weight;
                    float p_y = start2.Y + Vector3.Y * impulse.Y / MathF.Sqrt(slowModeValue) / start.Weight;
                    start.Start = start2.Set(p_x, p_y, start2.Z);
                }
                if (!end.IsFixed)
                {
                    float p_x2 = start3.X + Vector32.X * impulse.X / MathF.Sqrt(slowModeValue) / end.Weight;
                    float p_y2 = start3.Y + Vector32.Y * impulse.Y / MathF.Sqrt(slowModeValue) / end.Weight;
                    end.Start = start3.Set(p_x2, p_y2, start3.Z);
                }
            }
        }


        public void Decrease()
        {
            foreach (ModelNode node in _Nodes)
            {
                node.End.X = (node.End.X + node.Start.X) * 0.5f;
                node.End.Y = (node.End.Y + node.Start.Y) * 0.5f;
                node.End.Z = (node.End.Z + node.Start.Z) * 0.5f;
            }
        }
    }
}
