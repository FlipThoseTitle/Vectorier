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
using Vectorier.Gameplay.Trigger.Conditions;
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Trigger
{
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

        public TriggerLoop Parent => _Parent;

        protected TriggerCondition(TriggerLoop p_parent, XmlNode p_node)
        {
            _Parent = p_parent;
            _IsNot = p_node.Attributes["Not"].ParseBool();
        }

        public Variable GetOrCreateVar(string p_name)
        {
            if (p_name[0] == '_')
            {
                return GetVariable(p_name);
            }
            return MakeVariable(p_name);
        }

        private Variable MakeVariable(string p_name)
        {
            return Variable.createVariable(p_name, string.Empty, _Parent.ParentTrigger);
        }

        protected Variable GetVariable(string p_name)
        {
            string text = p_name.Substring(1, p_name.Length - 1);
            if (newNamesVars.ContainsKey(text))
            {
                text = newNamesVars[text];
            }
            return _Parent.ParentTrigger.GetVar("_" + text);
        }

        public abstract bool Check();

        public static TriggerCondition CreateConditions(TriggerLoop p_parent, XmlNode p_node, Dictionary<string, string> p_newNameVars)
        {
            newNamesVars = p_newNameVars;
            TriggerCondition triggerRunnerCondition = null;
            string name = p_node.Name;
            switch (name)
            {
                case "Equal":
                    return new TC_Equal(p_parent, p_node);
                case "Greater":
                    return new TC_Greater(p_parent, p_node);
                case "Less":
                    return new TC_Less(p_parent, p_node);
                case "GreaterEqual":
                    return new TC_GreaterEqual(p_parent, p_node);
                case "LessEqual":
                    return new TC_LessEqual(p_parent, p_node);
                case "Operator":
                    switch (p_node.Attributes["Type"].Value)
                    {
                        case "And":
                            return new TC_OperatorAnd(p_parent, p_node);
                        case "Or":
                            return new TC_OperatorOr(p_parent, p_node);
                        default:
                            return new TC_OperatorAnd(p_parent, p_node);
                    }
                default:
                    if (triggerRunnerCondition == null)
                    {
                        Console.WriteLine("Unknown condition " + name);
                    }
                    return triggerRunnerCondition;
            }
        }

        public static void ParseConditions(XmlNode p_node, TriggerLoop p_loop, List<TriggerCondition> p_conditions, string p_prefix = null)
        {
            if (p_node == null)
            {
                return;
            }
            XmlAttribute xmlAttribute = p_node.Attributes["Template"];
            if (xmlAttribute != null)
            {
                XmlNode templateConditionsXML = TemplateModule.getTemplateConditionsXML(xmlAttribute.Value);
                ParseConditions(templateConditionsXML, p_loop, p_conditions);
                return;
            }
            foreach (XmlNode childNode in p_node.ChildNodes)
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
                    ParseConditions(templateConditionsXML2, p_loop, p_conditions, p_prefix2);
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
                TriggerCondition triggerRunnerCondition = CreateConditions(p_loop, childNode, dictionary);
                if (triggerRunnerCondition != null)
                {
                    p_conditions.Add(triggerRunnerCondition);
                }
                newNamesVars = dictionary2;
            }
        }

        public override string ToString()
        {
            return "?";
        }
    }
}
