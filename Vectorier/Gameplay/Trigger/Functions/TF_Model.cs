#nullable disable warnings
using System;
using Vectorier.Gameplay.Camera;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Node;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Trigger.Functions;

public class TF_Model : TriggerFunction
{
    private delegate int ModelTFGeter();

    private TF_ModelNode _nodeFunc;

    private Variable paramVar;

    private ModelTFGeter _geter;

    public override int ValueInt
    {
        get
        {
            if (_nodeFunc != null)
            {
                var model = GetModel();
                return model == null ? NoValue : _nodeFunc.GetValue(model);
            }
            // No model yet ($Model is empty until a model enters the trigger) or no such model: no value.
            if (_geter == null || GetModel() == null)
            {
                return NoValue;
            }
            return _geter();
        }
    }

    public override float ValueFloat => ValueInt;

    public override string ValueString => ValueInt.ToString();

    public override string DebugStringValue
    {
        get
        {
            ModelHuman model = GetModel();
            if (model == null)
            {
                return "Model=null";
            }
            return ValueInt.ToString();
        }
    }

    /// <summary>Value of a query that has no model (or node) to read.</summary>
    public const int NoValue = 0;

    private string _missingLogged;

    public TF_Model(string value, string name, TriggerRunner parent)
        : base(name, parent)
    {
        _nodeFunc = null;
        _geter = null;
        string[] array = value.Split('.');
        int num = array[0].IndexOf("[") + 1;
        int num2 = array[0].LastIndexOf("]");
        string p_name2 = value.Substring(num, num2 - num);
        InitFuncVar(parent, ref paramVar, p_name2);
        string text = array.Length > 1 ? array[1] : string.Empty;
        switch (text)
        {
            case "AI":
                _geter = GetAI;
                break;
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
            case "direction":
                _geter = GetDirection;
                break;
            case "isControlled":
                _geter = GetIsControlled;
                break;
            case "isCameraFollow":
                _geter = GetIsCameraFollow;
                break;
            case "animationName":
                _geter = GetAnimationName;
                break;
            case "animationFrame":
                _geter = GetAnimationFrame;
                break;
            case "condition":
                _geter = GetCondition;
                break;
            default:
                if (text.IndexOf("getNode") != -1)
                {
                    _nodeFunc = new TF_ModelNode(text, array.Length > 2 ? array[2] : string.Empty, parent);
                }
                break;
        }
        if (_geter == null && _nodeFunc == null)
        {
            Console.WriteLine("TF_Func init error by geterName = " + text, true);
        }
    }

    public ModelHuman GetModel()
    {
        string name = paramVar?.ValueString;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null; // $Model before any model entered the trigger
        }
        ModelHuman modelByName = LevelMainController.current?.Location?.GetModelByName(name);
        if (modelByName == null && _missingLogged != name)
        {
            // Logged once per name: this is read every frame.
            _missingLogged = name;
            Console.WriteLine("No model named = \"" + name + "\" in trigger \"" + parent?.Name + "\"");
        }
        return modelByName;
    }

    /// <summary>COM of the model, or null when the model has no such node.</summary>
    private ModelNode Com() => GetModel()?.GetNode("COM");

    private int GetAI()
    {
        return GetModel()?.AI ?? NoValue;
    }

    private int GetLocalPositionX()
    {
        ModelNode modelNode = Com();
        return modelNode == null || parent == null ? NoValue : (int)(modelNode.Start.X - parent.LocalPosition.X);
    }

    private int GetLocalPositionY()
    {
        ModelNode modelNode = Com();
        return modelNode == null || parent == null ? NoValue : (int)(modelNode.Start.Y - parent.LocalPosition.Y);
    }

    private int GetWorldPositionX()
    {
        ModelNode modelNode = Com();
        return modelNode == null ? NoValue : (int)modelNode.Start.X;
    }

    private int GetWorldPositionY()
    {
        ModelNode modelNode = Com();
        return modelNode == null ? NoValue : (int)modelNode.Start.Y;
    }

    private int GetDirection()
    {
        ModelNode modelNode = Com();
        if (modelNode == null)
        {
            return NoValue;
        }
        return (modelNode.Start - modelNode.End).X > 0f ? 1 : -1;
    }

    private int GetCondition()
    {
        return 0;
    }

    private int GetAnimationName()
    {
        var model = GetModel();
        if (model?.ControllerAnimations?.Name == null)
        {
            return int.MaxValue;
        }
        return model.ControllerAnimations.Name.GetHashCode();
    }

    private int GetAnimationFrame()
    {
        return GetModel()?.ControllerAnimations?.CurrentFrame ?? NoValue;
    }

    private int GetIsControlled()
    {
        return GetModel()?.ControllerKeys?.Enable == true ? 1 : 0;
    }

    private int GetIsCameraFollow()
    {
        var cameraNode = GetModel()?.ModelObject?.CameraNode;
        return cameraNode != null && LocationCamera.Current?.Node != null && LocationCamera.Current.Node.Equals(cameraNode) ? 1 : 0;
    }
}
