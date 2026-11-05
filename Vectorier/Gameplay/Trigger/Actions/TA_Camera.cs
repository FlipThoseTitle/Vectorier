// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Vectorier.Gameplay.Camera;
using Vectorier.Gameplay.Models;

namespace Vectorier.Gameplay.Trigger.Actions
{
    public class TA_Camera : TriggerAction
    {
        private Variable _FollowVar;

        private Variable _ZoomVar;

        private Variable _SmoothnessVar;

        private Variable _EffectVar;

        private Variable _FramesVar;

        private Variable _IsStopVar;

        private TA_Camera(TA_Camera p_copyAction)
            : base(p_copyAction._ParentLoop)
        {
            _FollowVar = p_copyAction._FollowVar;
            _ZoomVar = p_copyAction._ZoomVar;
            _SmoothnessVar = p_copyAction._SmoothnessVar;
            _FramesVar = p_copyAction._FramesVar;
            _IsStopVar = p_copyAction._IsStopVar;
        }

        public TA_Camera(XmlNode p_node, TriggerLoop p_parent)
            : base(p_parent)
        {
            XmlAttribute xmlAttribute = p_node.Attributes["Zoom"];
            XmlAttribute xmlAttribute2 = p_node.Attributes["Smoothness"];
            XmlAttribute xmlAttribute3 = p_node.Attributes["Frames"];
            XmlAttribute xmlAttribute4 = p_node.Attributes["Follow"];
            XmlAttribute xmlAttribute5 = p_node.Attributes["Stop"];
            if (xmlAttribute != null)
            {
                InitActionVar(p_parent.ParentTrigger, ref _ZoomVar, xmlAttribute.Value);
            }
            if (xmlAttribute2 != null)
            {
                InitActionVar(p_parent.ParentTrigger, ref _SmoothnessVar, xmlAttribute2.Value);
            }
            if (xmlAttribute3 != null)
            {
                InitActionVar(p_parent.ParentTrigger, ref _FramesVar, xmlAttribute3.Value);
            }
            if (xmlAttribute4 != null)
            {
                InitActionVar(p_parent.ParentTrigger, ref _FollowVar, xmlAttribute4.Value);
            }
            if (xmlAttribute5 != null)
            {
                InitActionVar(p_parent.ParentTrigger, ref _IsStopVar, xmlAttribute5.Value);
            }
        }

        public override void Activate(ref bool p_isRunNext)
        {
            p_isRunNext = true;
            if (_FollowVar != null)
            {
                string valueString = _FollowVar.ValueString;
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
            if (_SmoothnessVar != null)
            {
                switch (_SmoothnessVar.Type)
                {
                    case VariableTypeE.VT_INT:
                        LocationCamera.FluencyCurrent = _SmoothnessVar.ValueInt;
                        break;
                    case VariableTypeE.VT_DOUBLE:
                        LocationCamera.FluencyCurrent = _SmoothnessVar.ValueFloat;
                        break;
                }
            }
            if (_ZoomVar != null)
            {
                float currentZoom = LocationCamera.CurrentZoom;
                switch (_ZoomVar.Type)
                {
                    case VariableTypeE.VT_INT:
                        LocationCamera.Current.Zooming(_ZoomVar.ValueInt * currentZoom);
                        break;
                    case VariableTypeE.VT_DOUBLE:
                        LocationCamera.Current.Zooming(_ZoomVar.ValueFloat * currentZoom);
                        break;
                }
            }
            if (_IsStopVar != null)
            {
                LocationCamera.Current.Stop();
            }
            if (_FramesVar != null)
            {
                //LocationCamera.Current.fra(_FramesVar.ValueInt);
            }
        }

        public override TriggerAction Copy()
        {
            return new TA_Camera(this);
        }

        public override string ToString()
        {
            string text = "Camera:";
            if (_FollowVar != null)
            {
                text = text + " Follow: " + _FollowVar.DebugStringValue + "|";
            }
            if (_SmoothnessVar != null)
            {
                switch (_SmoothnessVar.Type)
                {
                    case VariableTypeE.VT_INT:
                        LocationCamera.FluencyCurrent = _SmoothnessVar.ValueInt;
                        break;
                    case VariableTypeE.VT_DOUBLE:
                        LocationCamera.FluencyCurrent = _SmoothnessVar.ValueFloat;
                        break;
                }
                text = text + " Smoothness: " + _SmoothnessVar.DebugStringValue;
            }
            if (_ZoomVar != null)
            {
                text = text + " Zoom: " + _ZoomVar.DebugStringValue + "|";
            }
            if (_IsStopVar != null)
            {
                text += "Stop: 1";
            }
            return text;
        }
    }
}
