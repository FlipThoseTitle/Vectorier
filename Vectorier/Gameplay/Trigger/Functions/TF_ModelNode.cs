#nullable disable warnings
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Node;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Trigger.Functions;

public class TF_ModelNode : TriggerFunction
{
    private delegate int ModelNodeTFGeter(ModelHuman model);

    private Variable _paramVar;

    private ModelNodeTFGeter _geter;

    public TF_ModelNode(string value, string geterName, TriggerRunner parent)
        : base("", parent)
    {
        int num = value.IndexOf("[") + 1;
        int num2 = value.LastIndexOf("]");
        string p_name = value.Substring(num, num2 - num);
        InitFuncVar(parent, ref _paramVar, p_name);
        switch (geterName)
        {
            case "localPositionX":
                _geter = GetLocalPositionX;
                break;
            case "localPositionY":
                _geter = GetLocalPositionY;
                break;
            case "worldPositionX":
                _geter = GetWorldPositionX;
                break;
            case "worldPositionY":
                _geter = GetWorldPositionY;
                break;
        }
    }

    public int GetValue(ModelHuman model)
    {
        if (_geter == null || model == null)
        {
            return TF_Model.NoValue;
        }
        return _geter(model);
    }

    /// <summary>The named node, or null (no model object, unknown node name).</summary>
    private ModelNode Node(ModelHuman model)
    {
        string name = _paramVar?.ValueString;
        return string.IsNullOrWhiteSpace(name) || model.ModelObject == null ? null : model.GetNode(name);
    }

    private int GetLocalPositionX(ModelHuman model)
    {
        ModelNode modelNode = Node(model);
        return modelNode == null || parent == null ? TF_Model.NoValue : (int)(modelNode.Start.X - parent.LocalPosition.X);
    }

    private int GetLocalPositionY(ModelHuman model)
    {
        ModelNode modelNode = Node(model);
        return modelNode == null || parent == null ? TF_Model.NoValue : (int)(modelNode.Start.Y - parent.LocalPosition.Y);
    }

    private int GetWorldPositionX(ModelHuman model)
    {
        ModelNode modelNode = Node(model);
        return modelNode == null ? TF_Model.NoValue : (int)modelNode.Start.X;
    }

    private int GetWorldPositionY(ModelHuman model)
    {
        ModelNode modelNode = Node(model);
        return modelNode == null ? TF_Model.NoValue : (int)modelNode.Start.Y;
    }
}
