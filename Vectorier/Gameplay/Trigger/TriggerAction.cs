#nullable disable warnings
using System.Collections.Generic;
using System.Xml;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Runners;
using Vectorier.Gameplay.Trigger.Actions;
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Trigger;

public abstract class TriggerAction
{
    protected static Dictionary<string, string> newNamesVars;

    protected TriggerLoop _ParentLoop;

    public virtual int Frames => 0;

    public static TriggerAction CreateAction(XmlNode node, TriggerLoop parent, Dictionary<string, string> prefix)
    {
        newNamesVars = prefix;
        switch (node.Name)
        {
            case "Camera":
                return new TA_Camera(node, parent);
            case "Wait":
                return new TA_Wait(node, parent);
            case "SetVariable":
                return new TA_SetVariable(node, parent);
            case "AppendValue":
                return new TA_AppendValue(node, parent);
            case "Press":
                return new TA_Press(node, parent);
            case "ForceAnimation":
                return new TA_ForceAnimation(node, parent);
            case "Control":
                return new TA_Control(node, parent);
            case "EndGame":
                return new TA_EndGame(node, parent);
            case "SetTimer":
                return new TA_SetTimer(node, parent);
            case "Spawn":
                return new TA_Spawn(node, parent);
            case "Transform":
                return new TA_Transformation(node, parent, true);
            case "Choose":
                return new TA_Choose(node, parent);
            case "Activate":
                return new TA_Activate(node, parent);
            case "ModelExecute":
                return new TA_ModelExecute(node, parent);
            case "Kill":
                return new TA_Kill(node, parent);
            case "Sound":
                return new TA_Sound(node, parent);
            case "Tutorial":
                return new TA_Tutorial(node, parent);
            case "MessageOnScreen":
                return new TA_MessageOnScreen(node, parent);
            case "Execute":
                return new TA_Execute(node, parent);
            case "ShowLog":
                return new TA_ShowLog(node, parent);
            default:
                return null;
        }
    }

    public static void ParseActions(XmlNode node, TriggerLoop loop, List<TriggerAction> actions, string prefix = null)
    {
        if (node == null)
        {
            return;
        }
        XmlAttribute xmlAttribute = node.Attributes["Template"];
        if (xmlAttribute != null)
        {
            ParseActions(TemplateModule.getTemplateActionsXML(xmlAttribute.Value), loop, actions);
            return;
        }
        foreach (XmlNode childNode in node.ChildNodes)
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
                ParseActions(templateActionsXML, loop, actions, p_prefix2);
                continue;
            }
            Dictionary<string, string> dictionary = new Dictionary<string, string>();
            if (prefix != null)
            {
                XmlNode xmlNode2 = node.ParentNode["Using"];
                foreach (XmlNode childNode2 in xmlNode2.ChildNodes)
                {
                    if (childNode2.Attributes["ComplexName"] != null)
                    {
                        string value2 = childNode2.Attributes["Name"].Value;
                        string value3 = prefix + value2;
                        dictionary[value2] = value3;
                    }
                }
            }
            Dictionary<string, string> dictionary2 = newNamesVars;
            TriggerAction triggerRunnerAction = CreateAction(childNode, loop, dictionary);
            if (triggerRunnerAction != null)
            {
                actions.Add(triggerRunnerAction);
            }
            newNamesVars = dictionary2;
        }
    }

    private TriggerAction()
    {
    }

    protected TriggerAction(TriggerLoop parent)
    {
        _ParentLoop = parent;
    }

    public abstract TriggerAction Copy();

    public abstract void Activate(ref bool isRunNext);

    public ModelHuman GetModel()
    {
        return LevelMainController.current.GetModelByName(_ParentLoop.ParentTrigger.ModelVar.ValueString);
    }

    public ModelHuman GetModel(string modelName)
    {
        ModelHuman result = null;
        if (modelName.Length != 0 && modelName[0] == '_')
        {
            Variable parentVar = _ParentLoop.GetParentVar(modelName);
            if (parentVar != null)
            {
                result = LevelMainController.current.Location.GetModelByName(parentVar.ValueString);
            }
        }
        else
        {
            result = LevelMainController.current.Location.GetModelByName(modelName);
        }
        return result;
    }

    public virtual void Reset()
    {
    }

    private static Variable GetTriggerVar(TriggerRunner parent, string name)
    {
        string text = name.Substring(1);
        string value = string.Empty;
        if (newNamesVars.TryGetValue(text, out value))
        {
            text = value;
        }
        return parent.GetVar("_" + text);
    }

    protected static void InitActionVar(TriggerRunner parent, ref Variable var, string nameOrValue)
    {
        if (!string.IsNullOrEmpty(nameOrValue) && nameOrValue[0] == '_')
        {
            var = GetTriggerVar(parent, nameOrValue);
        }
        else
        {
            var = Variable.createVariable(nameOrValue, string.Empty, parent);
        }
    }

    public override string ToString()
    {
        return "Unknown TriggerAction";
    }
}
