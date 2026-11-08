#nullable disable warnings
using System.Xml;
using Vectorier.Gameplay.Camera;
using Vectorier.Gameplay.Models;

namespace Vectorier.Gameplay.Trigger.Actions;

public class TA_Camera : TriggerAction
{
    private Variable _followVar;

    private Variable _zoomVar;

    private Variable _smoothnessVar;
    private Variable _framesVar;

    private Variable _isStopVar;

    private TA_Camera(TA_Camera copyAction)
        : base(copyAction._ParentLoop)
    {
        _followVar = copyAction._followVar;
        _zoomVar = copyAction._zoomVar;
        _smoothnessVar = copyAction._smoothnessVar;
        _framesVar = copyAction._framesVar;
        _isStopVar = copyAction._isStopVar;
    }

    public TA_Camera(XmlNode node, TriggerLoop parent)
        : base(parent)
    {
        XmlAttribute xmlAttribute = node.Attributes["Zoom"];
        XmlAttribute xmlAttribute2 = node.Attributes["Smoothness"];
        XmlAttribute xmlAttribute3 = node.Attributes["Frames"];
        XmlAttribute xmlAttribute4 = node.Attributes["Follow"];
        XmlAttribute xmlAttribute5 = node.Attributes["Stop"];
        if (xmlAttribute != null)
        {
            InitActionVar(parent.ParentTrigger, ref _zoomVar, xmlAttribute.Value);
        }
        if (xmlAttribute2 != null)
        {
            InitActionVar(parent.ParentTrigger, ref _smoothnessVar, xmlAttribute2.Value);
        }
        if (xmlAttribute3 != null)
        {
            InitActionVar(parent.ParentTrigger, ref _framesVar, xmlAttribute3.Value);
        }
        if (xmlAttribute4 != null)
        {
            InitActionVar(parent.ParentTrigger, ref _followVar, xmlAttribute4.Value);
        }
        if (xmlAttribute5 != null)
        {
            InitActionVar(parent.ParentTrigger, ref _isStopVar, xmlAttribute5.Value);
        }
    }

    public override void Activate(ref bool isRunNext)
    {
        isRunNext = true;
        if (_followVar != null)
        {
            string valueString = _followVar.ValueString;
            ModelHuman model = GetModel(valueString);
            if (model != null)
            {
                LocationCamera.Current.Node = model.ModelObject.CameraNode;
            }
            else
            {
                LocationCamera.Current.Stop();
            }
        }
        if (_smoothnessVar != null)
        {
            switch (_smoothnessVar.Type)
            {
                case VariableTypeE.VT_INT:
                    LocationCamera.FluencyCurrent = _smoothnessVar.ValueInt;
                    break;
                case VariableTypeE.VT_DOUBLE:
                    LocationCamera.FluencyCurrent = _smoothnessVar.ValueFloat;
                    break;
            }
        }
        if (_zoomVar != null)
        {
            float currentZoom = LocationCamera.CurrentZoom;
            switch (_zoomVar.Type)
            {
                case VariableTypeE.VT_INT:
                    LocationCamera.Current.Zooming(_zoomVar.ValueInt * currentZoom);
                    break;
                case VariableTypeE.VT_DOUBLE:
                    LocationCamera.Current.Zooming(_zoomVar.ValueFloat * currentZoom);
                    break;
            }
        }
        if (_isStopVar != null)
        {
            LocationCamera.Current.Stop();
        }
        if (_framesVar != null)
        {
        }
    }

    public override TriggerAction Copy()
    {
        return new TA_Camera(this);
    }

    public override string ToString()
    {
        string text = "Camera:";
        if (_followVar != null)
        {
            text = text + " Follow: " + _followVar.DebugStringValue + "|";
        }
        if (_smoothnessVar != null)
        {
            switch (_smoothnessVar.Type)
            {
                case VariableTypeE.VT_INT:
                    LocationCamera.FluencyCurrent = _smoothnessVar.ValueInt;
                    break;
                case VariableTypeE.VT_DOUBLE:
                    LocationCamera.FluencyCurrent = _smoothnessVar.ValueFloat;
                    break;
            }
            text = text + " Smoothness: " + _smoothnessVar.DebugStringValue;
        }
        if (_zoomVar != null)
        {
            text = text + " Zoom: " + _zoomVar.DebugStringValue + "|";
        }
        if (_isStopVar != null)
        {
            text += "Stop: 1";
        }
        return text;
    }
}
