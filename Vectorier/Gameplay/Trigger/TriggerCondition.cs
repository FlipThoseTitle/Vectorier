#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Xml;
using Vectorier.Gameplay.Trigger.Conditions;
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Trigger;

public abstract class TriggerCondition
{
    public enum ConditionsType
    {
        TCT_None,
        TCT_Equal,
        TCT_OperatorOr,
        TCT_OperatorAnd,
        TCT_Greater,
        TCT_Less,
        TCT_GreaterEqual,
        TCT_LessEqual
    }

    protected static Dictionary<string, string> newNamesVars;

    protected TriggerLoop _Parent;

    protected ConditionsType _Type = ConditionsType.TCT_None;

    protected bool _IsNot;

    protected TriggerCondition(TriggerLoop parent, XmlNode node)
    {
        _Parent = parent;
        _IsNot = node.Attributes["Not"].ParseBool();
    }

    public Variable GetOrCreateVar(string name)
    {
        if (name[0] == '_')
        {
            return GetVariable(name);
        }
        return MakeVariable(name);
    }

    private Variable MakeVariable(string name)
    {
        return Variable.createVariable(name, string.Empty, _Parent.ParentTrigger);
    }

    protected Variable GetVariable(string name)
    {
        string text = name.Substring(1, name.Length - 1);
        if (newNamesVars.ContainsKey(text))
        {
            text = newNamesVars[text];
        }
        return _Parent.ParentTrigger.GetVar("_" + text);
    }

    public abstract bool Check();

    public static TriggerCondition CreateConditions(TriggerLoop parent, XmlNode node, Dictionary<string, string> newNameVars)
    {
        newNamesVars = newNameVars;
        TriggerCondition triggerRunnerCondition = null;
        string name = node.Name;
        switch (name)
        {
            case "Equal":
                return new TC_Equal(parent, node);
            case "Greater":
                return new TC_Greater(parent, node);
            case "Less":
                return new TC_Less(parent, node);
            case "GreaterEqual":
                return new TC_GreaterEqual(parent, node);
            case "LessEqual":
                return new TC_LessEqual(parent, node);
            case "Operator":
                switch (node.Attributes["Type"].Value)
                {
                    case "And":
                        return new TC_OperatorAnd(parent, node);
                    case "Or":
                        return new TC_OperatorOr(parent, node);
                    default:
                        return new TC_OperatorAnd(parent, node);
                }
            default:
                if (triggerRunnerCondition == null)
                {
                    Diagnostics.GameLog.Warning("Loading the level's triggers", $"Unknown trigger condition <{name}>: it is ignored.", node: node);
                }
                return triggerRunnerCondition;
        }
    }

    public static void ParseConditions(XmlNode node, TriggerLoop loop, List<TriggerCondition> conditions, string prefix = null)
    {
        if (node == null)
        {
            return;
        }
        XmlAttribute xmlAttribute = node.Attributes["Template"];
        if (xmlAttribute != null)
        {
            XmlNode templateConditionsXML = TemplateModule.getTemplateConditionsXML(xmlAttribute.Value);
            ParseConditions(templateConditionsXML, loop, conditions);
            return;
        }
        foreach (XmlNode childNode in node.ChildNodes)
        {
            if (childNode.Name.Equals("#comment"))
            {
                continue;
            }
            if (childNode.Name.Equals("ConditionBlock"))
            {
                string value = childNode.Attributes["Template"].Value;
                XmlNode templateConditionsXML2 = TemplateModule.getTemplateConditionsXML(value);
                string p_prefix2 = childNode.Attributes["Prefix"] == null ? null : childNode.Attributes["Prefix"].Value;
                ParseConditions(templateConditionsXML2, loop, conditions, p_prefix2);
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
            TriggerCondition triggerRunnerCondition = CreateConditions(loop, childNode, dictionary);
            if (triggerRunnerCondition != null)
            {
                conditions.Add(triggerRunnerCondition);
            }
            newNamesVars = dictionary2;
        }
    }

    public override string ToString()
    {
        return "?";
    }
}
