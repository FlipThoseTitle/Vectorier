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
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Runners;
using Vectorier.Gameplay.Trigger.Actions;
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Trigger
{
    public abstract class TriggerAction
    {
        protected static Dictionary<string, string> newNamesVars;

        protected TriggerLoop _ParentLoop;

        private TriggerAction _Action;

        public virtual int Frames => 0;

        public static TriggerAction CreateAction(XmlNode p_node, TriggerLoop p_parent, Dictionary<string, string> p_prefix)
        {
            newNamesVars = p_prefix;
            switch (p_node.Name)
            {
                case "Camera":
                    return new TA_Camera(p_node, p_parent);
                case "Wait":
                    return new TA_Wait(p_node, p_parent);
                case "SetVariable":
                    return new TA_SetVariable(p_node, p_parent);
                case "AppendValue":
                    return new TA_AppendValue(p_node, p_parent);
                case "Press":
                    return new TA_Press(p_node, p_parent);
                case "ForceAnimation":
                    return new TA_ForceAnimation(p_node, p_parent);
                case "Control":
                    return new TA_Control(p_node, p_parent);
                case "EndGame":
                    return new TA_EndGame(p_node, p_parent);
                case "SetTimer":
                    return new TA_SetTimer(p_node, p_parent);
                case "Spawn":
                    return new TA_Spawn(p_node, p_parent);
                case "Transform":
                    return new TA_Transformation(p_node, p_parent, true);
                case "Choose":
                    return new TA_Choose(p_node, p_parent);
                case "Activate":
                    return new TA_Activate(p_node, p_parent);
                case "ModelExecute":
                    return new TA_ModelExecute(p_node, p_parent);
                case "Kill":
                    return new TA_Kill(p_node, p_parent);
                case "Sound":
                    return new TA_Sound(p_node, p_parent);
                case "Tutorial":
                    return new TA_Tutorial(p_node, p_parent);
                case "MessageOnScreen":
                    return new TA_MessageOnScreen(p_node, p_parent);
                case "Execute":
                    return new TA_Execute(p_node, p_parent);
                case "ShowLog":
                    return new TA_ShowLog(p_node, p_parent);
                default:
                    return null;
            }
        }

        public static void ParseActions(XmlNode p_node, TriggerLoop p_loop, List<TriggerAction> p_actions, string p_prefix = null)
        {
            if (p_node == null)
            {
                return;
            }
            XmlAttribute xmlAttribute = p_node.Attributes["Template"];
            if (xmlAttribute != null)
            {
                ParseActions(TemplateModule.getTemplateActionsXML(xmlAttribute.Value), p_loop, p_actions);
                return;
            }
            foreach (XmlNode childNode in p_node.ChildNodes)
            {
                if (childNode.LocalName.Equals("#comment"))
                {
                    continue;
                }
                if (childNode.LocalName.Equals("ActionBlock"))
                {
                    string value = childNode.Attributes["Template"].Value;
                    XmlNode templateActionsXML = TemplateModule.getTemplateActionsXML(value);
                    string p_prefix2 = childNode.Attributes["Prefix"].ParseString();
                    ParseActions(templateActionsXML, p_loop, p_actions, p_prefix2);
                    continue;
                }
                Dictionary<string, string> dictionary = new Dictionary<string, string>();
                if (p_prefix != null)
                {
                    XmlNode xmlNode2 = p_node.ParentNode["Using"];
                    foreach (XmlNode childNode2 in xmlNode2.ChildNodes)
                    {
                        if (childNode2.Attributes["ComplexName"] != null)
                        {
                            string value2 = childNode2.Attributes["Name"].Value;
                            string value3 = p_prefix + value2;
                            dictionary[value2] = value3;
                        }
                    }
                }
                Dictionary<string, string> dictionary2 = newNamesVars;
                TriggerAction triggerRunnerAction = CreateAction(childNode, p_loop, dictionary);
                if (triggerRunnerAction != null)
                {
                    p_actions.Add(triggerRunnerAction);
                }
                newNamesVars = dictionary2;
            }
        }

        private TriggerAction()
        {
        }

        protected TriggerAction(TriggerLoop p_parent)
        {
            _ParentLoop = p_parent;
        }

        public abstract TriggerAction Copy();

        public abstract void Activate(ref bool isRunNext);

        public ModelHuman GetModel()
        {
            return LevelMainController.current.GetModelByName(_ParentLoop.ParentTrigger.ModelVar.ValueString);
        }

        public ModelHuman GetModel(string p_modelName)
        {
            ModelHuman result = null;
            if (p_modelName.Length != 0 && p_modelName[0] == '_')
            {
                Variable parentVar = _ParentLoop.GetParentVar(p_modelName);
                if (parentVar != null)
                {
                    result = LevelMainController.current.Location.GetModelByName(parentVar.ValueString);
                }
            }
            else
            {
                result = LevelMainController.current.Location.GetModelByName(p_modelName);
            }
            return result;
        }

        public virtual void Reset()
        {
        }

        private static Variable GetTriggerVar(TriggerRunner p_parent, string p_name)
        {
            string text = p_name.Substring(1);
            string value = string.Empty;
            if (newNamesVars.TryGetValue(text, out value))
            {
                text = value;
            }
            return p_parent.GetVar("_" + text);
        }

        protected static void InitActionVar(TriggerRunner p_parent, ref Variable p_var, string p_nameOrValue)
        {
            if (!string.IsNullOrEmpty(p_nameOrValue) && p_nameOrValue[0] == '_')
            {
                p_var = GetTriggerVar(p_parent, p_nameOrValue);
            }
            else
            {
                p_var = Variable.createVariable(p_nameOrValue, string.Empty, p_parent);
            }
        }

        public override string ToString()
        {
            return "Unknown TriggerAction";
        }
    }
}
