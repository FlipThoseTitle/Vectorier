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
using Vectorier.Gameplay.Utilities;

namespace Vectorier.Gameplay.Trigger
{
    public class TemplateModule
    {
        private static TemplateModule current;

        /// <summary>The editor's addition: TriggerTemplates.xml is read again for the next game (another project).</summary>
        public static void Reload() => current = null; // read when first used

        private Dictionary<string, Template> templates = new Dictionary<string, Template>();

        private XmlDocument document;

        public TemplateModule()
        {
            document = XmlUtils.OpenXMLDocument(VectorPaths.Xml, "TriggerTemplates.xml");
            parseTemplates();
        }

        public static XmlNode getTemplateXmlNode(string p_name)
        {
            if (!(current ??= new TemplateModule()).templates.ContainsKey(p_name))
            {
                return null;
            }
            return (current ??= new TemplateModule()).templates[p_name].mainTemplateNode;
        }

        public void parseTemplates()
        {
            XmlNode xmlNode = document?["Templates"];
            if (xmlNode == null)
            {
                return; // a project without TriggerTemplates.xml: no templates
            }
            foreach (XmlNode childNode in xmlNode.ChildNodes)
            {
                string value = childNode.Attributes["Name"].Value;
                Template value2 = new Template(childNode);
                templates[value] = value2;
            }
        }

        public static XmlNode getTemplateActionsXML(string p_name)
        {
            string[] array = p_name.Split('.');
            Template template = (current ??= new TemplateModule()).templates[array[0]];
            foreach (XmlNode childNode in template.mainTemplateNode.ChildNodes)
            {
                if (childNode.Name.Equals("Loop") && childNode.Attributes["Name"].Value.Equals(array[1]))
                {
                    return childNode["Actions"];
                }
            }
            return null;
        }

        public static XmlNode getTemplateConditionsXML(string p_name)
        {
            string[] array = p_name.Split('.');
            Template template = (current ??= new TemplateModule()).templates[array[0]];
            foreach (XmlNode childNode in template.mainTemplateNode.ChildNodes)
            {
                if (childNode.Name.Equals("Loop") && childNode.Attributes["Name"].Value.Equals(array[1]))
                {
                    return childNode["Conditions"];
                }
            }
            return null;
        }

        public static XmlNode getTemplateLoopXML(string p_name)
        {
            string[] array = p_name.Split('.');
            Template template = (current ??= new TemplateModule()).templates[array[0]];
            foreach (XmlNode childNode in template.mainTemplateNode.ChildNodes)
            {
                if (childNode.Name.Equals("Loop") && childNode.Attributes["Name"].Value.Equals(array[1]))
                {
                    return childNode;
                }
            }
            return null;
        }

        public static XmlNode getTemplateEventsXml(string p_name)
        {
            string[] array = p_name.Split('.');
            Template template = (current ??= new TemplateModule()).templates[array[0]];
            if (template == null)
            {
                return null;
            }
            foreach (XmlNode childNode in template.mainTemplateNode.ChildNodes)
            {
                if (childNode.Name.Equals("Loop") && childNode.Attributes["Name"].Value.Equals(array[1]))
                {
                    return childNode["Events"];
                }
            }
            return null;
        }
    }
}
