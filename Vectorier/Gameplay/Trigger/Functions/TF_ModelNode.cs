// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Node;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Trigger.Functions
{
    public class TF_ModelNode : TriggerFunction
    {
        private delegate int ModelNodeTFGeter(ModelHuman p_model);

        private Variable _ParamVar;

        private ModelNodeTFGeter _Geter;

        public TF_ModelNode(string p_value, string p_geterName, TriggerRunner p_parent)
            : base("", p_parent)
        {
            int num = p_value.IndexOf("[") + 1;
            int num2 = p_value.LastIndexOf("]");
            string p_name = p_value.Substring(num, num2 - num);
            InitFuncVar(p_parent, ref _ParamVar, p_name);
            switch (p_geterName)
            {
                case "localPositionX":
                    _Geter = GetLocalPositionX;
                    break;
                case "localPositionY":
                    _Geter = GetLocalPositionY;
                    break;
                case "worldPositionX":
                    _Geter = GetWorldPositionX;
                    break;
                case "worldPositionY":
                    _Geter = GetWorldPositionY;
                    break;
            }
        }

        public int GetValue(ModelHuman p_model)
        {
            if (_Geter == null || p_model == null)
            {
                return TF_Model.NoValue;
            }
            return _Geter(p_model);
        }

        /// <summary>The named node, or null (no model object, unknown node name).</summary>
        private ModelNode Node(ModelHuman p_model)
        {
            string name = _ParamVar?.ValueString;
            return string.IsNullOrWhiteSpace(name) || p_model.ModelObject == null ? null : p_model.GetNode(name);
        }

        private int GetLocalPositionX(ModelHuman p_model)
        {
            ModelNode modelNode = Node(p_model);
            return modelNode == null || parent == null ? TF_Model.NoValue : (int)(modelNode.Start.X - parent.LocalPosition.X);
        }

        private int GetLocalPositionY(ModelHuman p_model)
        {
            ModelNode modelNode = Node(p_model);
            return modelNode == null || parent == null ? TF_Model.NoValue : (int)(modelNode.Start.Y - parent.LocalPosition.Y);
        }

        private int GetWorldPositionX(ModelHuman p_model)
        {
            ModelNode modelNode = Node(p_model);
            return modelNode == null ? TF_Model.NoValue : (int)modelNode.Start.X;
        }

        private int GetWorldPositionY(ModelHuman p_model)
        {
            ModelNode modelNode = Node(p_model);
            return modelNode == null ? TF_Model.NoValue : (int)modelNode.Start.Y;
        }
    }
}
