// Ported from Vector-Raylib-For-Vectorier (the game's player movement), see Gameplay/README.md.
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Node;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Controllers
{
    public class ControllerAreas
    {
        private ModelHuman _Model;

        private ModelNode _ComNode;

        private List<AreaRunner> _ActiveAreas = new List<AreaRunner>();

        public List<AreaRunner> ActiveAreas => _ActiveAreas;

        public ControllerAreas(ModelHuman model)
        {
            _Model = model;
            _ComNode = model.GetNode("COM");
        }

        private bool IsInside(AreaRunner p_value)
        {
            return p_value.Hit(_ComNode.Start);
        }

        private bool IsActive(AreaRunner p_value)
        {
            return ActiveAreas.Contains(p_value);
        }

        private void Activate(AreaRunner p_value)
        {
            _ActiveAreas.Add(p_value);
            p_value.Activate(_Model);
            _Model.OnActiveArea(p_value);
        }

        private void Deactivate(AreaRunner p_value)
        {
            _Model.CheckDelayAction(p_value);
            p_value.Deactivate(_Model);
            _ActiveAreas.Remove(p_value);
        }

        public void Check(AreaRunner p_value)
        {
            bool flag = IsInside(p_value);
            if (IsActive(p_value))
            {
                if (!flag)
                {
                    Deactivate(p_value);
                }
            }
            else if (flag)
            {
                Activate(p_value);
            }
        }

        public void Reset()
        {
            _ActiveAreas.Clear();
        }
    }
}
